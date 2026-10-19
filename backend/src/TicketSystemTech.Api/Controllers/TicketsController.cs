using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITicketNumberGenerator _ticketNumberGenerator;
    private readonly IEmailSender _emailSender;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IKnowledgeBaseIndexer _knowledgeBaseIndexer;
    private readonly IPiiProtector _piiProtector;
    private readonly IHtmlSanitizerService _html;
    private readonly IFileLinkService _links;
    private readonly ISlaCalculator _sla;
    private readonly ILogger<TicketsController> _logger;

    public TicketsController(
        AppDbContext db,
        ICurrentUserService currentUser,
        ITicketNumberGenerator ticketNumberGenerator,
        IEmailSender emailSender,
        INotificationPublisher notificationPublisher,
        IKnowledgeBaseIndexer knowledgeBaseIndexer,
        IPiiProtector piiProtector,
        IHtmlSanitizerService html,
        IFileLinkService links,
        ISlaCalculator sla,
        ILogger<TicketsController> logger)
    {
        _links = links;
        _sla = sla;
        _logger = logger;
        _db = db;
        _currentUser = currentUser;
        _ticketNumberGenerator = ticketNumberGenerator;
        _emailSender = emailSender;
        _notificationPublisher = notificationPublisher;
        _knowledgeBaseIndexer = knowledgeBaseIndexer;
        _piiProtector = piiProtector;
        _html = html;
    }

    // Names/branches are user-controlled text interpolated into HTML the server itself builds
    // (system-event feed entries), so they must be encoded.
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
    private static string FullName(ApplicationUser u) => $"{H(u.FirstName)} {H(u.LastName)}";

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Client))]
    public async Task<ActionResult<TicketDetailDto>> Create(CreateTicketRequest request)
    {
        var clientId = _currentUser.UserId!.Value;

        var (idempotencyKey, keyError) = ReadIdempotencyKey();
        if (keyError is not null) return keyError;
        if (idempotencyKey is not null && await FindPreviousResultAsync(clientId, idempotencyKey) is { } previous)
            return Ok(await BuildDetailDto(previous)); // a retry of a request that already succeeded

        var companyId = _currentUser.CompanyId;
        if (companyId is null)
            return BadRequest(new { message = "Your account is not linked to a company." });

        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.IsActive);
        if (department is null) return BadRequest(new { message = "Please select a valid branch." });

        var client = await _db.Users.FirstAsync(u => u.Id == clientId);
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId.Value);

        var ticket = new Ticket
        {
            TicketNumber = await _ticketNumberGenerator.NextAsync(),
            Title = request.Title,
            Description = _html.Sanitize(request.Description),
            ClientId = clientId,
            CompanyId = companyId.Value,
            DepartmentId = department.Id,
            Status = TicketStatus.New
        };
        _db.Tickets.Add(ticket);

        // Ticket is routed to the branch the client picked — only that branch's staff are notified/emailed for triage.
        var staff = await _db.Users.Where(u => u.Role == UserRole.Employee && u.IsActive && u.DepartmentId == department.Id).ToListAsync();
        foreach (var member in staff)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = member.Id,
                Type = NotificationType.NewTicket,
                Message = $"New ticket #{ticket.TicketNumber}: {ticket.Title}",
                TicketId = ticket.Id
            });
        }

        // The ticket, its in-app notifications and the idempotency record commit together or not at all, so a
        // retry can never find a ticket without its notifications (or notifications without a ticket).
        var raced = await SaveCreatedAsync(clientId, idempotencyKey, ticket.Id);
        if (raced is { } winner) return Ok(await BuildDetailDto(winner));

        foreach (var member in staff)
        {
            await _emailSender.SendAsync(member.Email!, $"New ticket #{ticket.TicketNumber} submitted — {company.Name}",
                EmailTemplates.NewTicketSubmitted(member.FirstName, ticket.TicketNumber, ticket.Title, $"{client.FirstName} {client.LastName}", company.Name));
            await _notificationPublisher.PushToUserAsync(member.Id, "newTicket", new { ticket.Id, ticket.TicketNumber, ticket.Title });
        }

        return Ok(await BuildDetailDto(ticket.Id));
    }

    /// <summary>
    /// Staff creates a ticket on behalf of a client who reported the issue verbally (e.g. by phone) rather
    /// than through the client portal. The ticket is created and opened in one step, and appears exactly as
    /// if the client had submitted it themselves.
    /// </summary>
    [HttpPost("on-behalf")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> CreateOnBehalf(CreateTicketOnBehalfRequest request)
    {
        var (idempotencyKey, keyError) = ReadIdempotencyKey();
        if (keyError is not null) return keyError;
        if (idempotencyKey is not null && await FindPreviousResultAsync(_currentUser.UserId!.Value, idempotencyKey) is { } previous)
            return Ok(await BuildDetailDto(previous));

        var clientEmailHash = _piiProtector.Hash(request.ClientEmail);
        var client = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == clientEmailHash && u.Role == UserRole.Client);
        if (client is null) return NotFound(new { message = "No client account found with that email." });
        if (client.CompanyId is null) return BadRequest(new { message = "This client is not linked to a company." });

        // Non-admin staff can only route a ticket into their own branch; moving it elsewhere later requires an explicit transfer.
        if (_currentUser.Role != UserRole.Admin && request.DepartmentId != _currentUser.DepartmentId)
            return BadRequest(new { message = "You can only open tickets into your own branch." });

        var subBranchError = await ValidateSubBranchAsync(request.DepartmentId, request.SubBranchId);
        if (subBranchError is not null) return BadRequest(new { message = subBranchError });

        var assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId
            && u.Role == UserRole.Employee);
        if (assignee is null) return BadRequest(new { message = "Assignee must be a support agent or consultant." });
        if (assignee.DepartmentId != request.DepartmentId)
            return BadRequest(new { message = "Assignee must belong to the selected branch." });
        if (request.SubBranchId.HasValue && assignee.SubBranchId != request.SubBranchId)
            return BadRequest(new { message = "Assignee must belong to the selected sub-branch." });

        var openedAtUtc = DateTime.UtcNow;
        var (dueDateUtc, slaError) = await ResolveDueDateAsync(request.SlaPlanId, request.DueDateUtc, openedAtUtc);
        if (slaError is not null) return BadRequest(new { message = slaError });

        var ticket = new Ticket
        {
            TicketNumber = await _ticketNumberGenerator.NextAsync(),
            Title = request.Title,
            Description = _html.Sanitize(request.Description),
            ClientId = client.Id,
            CompanyId = client.CompanyId.Value,
            Status = TicketStatus.Open,
            Source = request.Source,
            HelpTopicId = request.HelpTopicId,
            DepartmentId = request.DepartmentId,
            SubBranchId = request.SubBranchId,
            SlaPlanId = request.SlaPlanId,
            Priority = request.Priority,
            DueDateUtc = dueDateUtc,
            CategoryId = request.CategoryId,
            AssignedToUserId = request.AssignedToUserId,
            OpenedByUserId = _currentUser.UserId,
            OpenedAtUtc = openedAtUtc
        };
        _db.Tickets.Add(ticket);
        _db.TicketAssignees.Add(new TicketAssignee { Ticket = ticket, UserId = assignee.Id });

        var openedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            Ticket = ticket,
            AuthorId = openedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket opened by {FullName(openedBy)} and assigned to {FullName(assignee)}.</p>"
        });

        _db.Notifications.Add(new Notification { UserId = assignee.Id, Type = NotificationType.TicketAssigned, Message = $"Ticket #{ticket.TicketNumber} assigned to you", TicketId = ticket.Id });
        _db.Notifications.Add(new Notification { UserId = client.Id, Type = NotificationType.TicketOpened, Message = $"Your ticket #{ticket.TicketNumber} has been opened", TicketId = ticket.Id });

        var raced = await SaveCreatedAsync(_currentUser.UserId!.Value, idempotencyKey, ticket.Id);
        if (raced is { } winner) return Ok(await BuildDetailDto(winner));

        await _emailSender.SendAsync(client.Email!, $"Your ticket #{ticket.TicketNumber} has been opened", EmailTemplates.TicketOpened(client.FirstName, ticket.TicketNumber));
        await _emailSender.SendAsync(assignee.Email!, $"Ticket #{ticket.TicketNumber} assigned to you", EmailTemplates.TicketAssigned(assignee.FirstName, ticket.TicketNumber, ticket.Title));
        await _notificationPublisher.PushToUserAsync(assignee.Id, "ticketAssigned", new { ticket.Id, ticket.TicketNumber, ticket.Title });
        await _notificationPublisher.PushToUserAsync(client.Id, "ticketOpened", new { ticket.Id, ticket.TicketNumber, ticket.Title });

        return Ok(await BuildDetailDto(ticket.Id));
    }

    private const string TicketCreateScope = "ticket-create";

    /// <summary>Reads the optional Idempotency-Key header. A present but malformed key is an error, never silently ignored.</summary>
    private (string? Key, BadRequestObjectResult? Error) ReadIdempotencyKey()
    {
        if (!Request.Headers.TryGetValue(IdempotencyKey.HeaderName, out var values)) return (null, null);

        var key = values.ToString();
        return IdempotencyKey.IsValid(key)
            ? (key, null)
            : (null, BadRequest(new { message = $"{IdempotencyKey.HeaderName} must be 1-{IdempotencyKey.MaxLength} letters, digits, '-' or '_'." }));
    }

    private Task<Guid?> FindPreviousResultAsync(Guid userId, string key) =>
        _db.IdempotencyRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.Scope == TicketCreateScope && r.Key == key)
            .Select(r => (Guid?)r.ResourceId)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Commits everything staged on the context. When a key was supplied its record goes in the same transaction;
    /// if another request with the same key won the race the unique index rejects this one, nothing is written, and
    /// the winner's ticket id is returned so the caller can hand that back instead.
    /// </summary>
    private async Task<Guid?> SaveCreatedAsync(Guid userId, string? key, Guid ticketId)
    {
        if (key is null)
        {
            await _db.SaveChangesAsync();
            return null;
        }

        _db.IdempotencyRecords.Add(new IdempotencyRecord { UserId = userId, Scope = TicketCreateScope, Key = key, ResourceId = ticketId });
        try
        {
            await _db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var winner = await FindPreviousResultAsync(userId, key);
            if (winner is null) throw; // not the idempotency index: a genuine failure
            return winner;
        }
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TicketListItem>>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? subBranchId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] bool answeredOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Tickets.AsNoTracking().AsQueryable();

        query = _currentUser.Role switch
        {
            UserRole.Client => query.Where(t => t.ClientId == _currentUser.UserId),
            UserRole.Admin => query, // Admin sees everything across all branches
            // Branches are isolated: staff see unrouted (New) tickets for triage, plus tickets already routed to their own branch.
            _ => query.Where(t => t.DepartmentId == null || t.DepartmentId == _currentUser.DepartmentId)
        };

        var statuses = ParseStatuses(status);
        if (statuses.Count > 0) query = query.Where(t => statuses.Contains(t.Status));
        if (companyId.HasValue) query = query.Where(t => t.CompanyId == companyId.Value);
        if (clientId.HasValue) query = query.Where(t => t.ClientId == clientId.Value);
        if (assignedToUserId.HasValue)
            query = query.Where(t => t.AssignedToUserId == assignedToUserId.Value || t.Assignees.Any(a => a.UserId == assignedToUserId.Value));
        if (departmentId.HasValue) query = query.Where(t => t.DepartmentId == departmentId.Value);
        if (subBranchId.HasValue) query = query.Where(t => t.SubBranchId == subBranchId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.TicketNumber.Contains(search) || t.Title.Contains(search));
        if (createdFrom.HasValue) query = query.Where(t => t.CreatedAt >= createdFrom.Value);
        if (createdTo.HasValue) query = query.Where(t => t.CreatedAt <= createdTo.Value);
        if (answeredOnly)
        {
            // "Answered" = an open (never Closed/New) ticket that a staff member has already replied to —
            // e.g. staff called the client and the client asked to follow up tomorrow, so it's not done yet.
            var staffIds = await _db.Users.Where(u => u.Role != UserRole.Client).Select(u => u.Id).ToListAsync();
            query = query.Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.New &&
                t.Messages.Any(m => m.Type == MessageType.Response && staffIds.Contains(m.AuthorId)));
        }

        var totalCount = await query.CountAsync();

        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "ticketnumber" => desc ? query.OrderByDescending(t => t.TicketNumber) : query.OrderBy(t => t.TicketNumber),
            "title" => desc ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
            "status" => desc ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "priority" => desc ? query.OrderByDescending(t => t.Priority) : query.OrderBy(t => t.Priority),
            "duedate" => desc ? query.OrderByDescending(t => t.DueDateUtc) : query.OrderBy(t => t.DueDateUtc),
            "createdat" => desc ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
            "branch" => desc ? query.OrderByDescending(t => t.Department!.Name) : query.OrderBy(t => t.Department!.Name),
            "company" => desc ? query.OrderByDescending(t => t.Company!.Name) : query.OrderBy(t => t.Company!.Name),
            // Default: most urgent first (Emergency, High, Medium, Low), then newest created first.
            _ => query.OrderBy(t => t.Priority).ThenByDescending(t => t.CreatedAt)
        };

        var page_ = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new
            {
                t.Id, t.TicketNumber, t.Title, t.Status, t.Priority, t.CompanyId, t.ClientId, t.AssignedToUserId, t.CreatedAt, t.DueDateUtc, t.ClosedAtUtc, t.DepartmentId, t.SubBranchId
            })
            .ToListAsync();

        var userIds = page_.SelectMany(t => new[] { t.ClientId, t.AssignedToUserId }).Where(id => id.HasValue).Select(id => id!.Value)
            .Concat(page_.Select(t => t.ClientId)).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName);
        var companyIds = page_.Select(t => t.CompanyId).Distinct().ToList();
        var companies = await _db.Companies.Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        var departmentIds = page_.Where(t => t.DepartmentId.HasValue).Select(t => t.DepartmentId!.Value).Distinct().ToList();
        var departments = await _db.Departments.Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name);
        var subBranchIds = page_.Where(t => t.SubBranchId.HasValue).Select(t => t.SubBranchId!.Value).Distinct().ToList();
        var subBranches = await _db.SubBranches.Where(s => subBranchIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);

        var items = page_.Select(t => new TicketListItem(
            t.Id, t.TicketNumber, t.Title, t.Status.ToString(), t.Priority?.ToString(),
            companies.GetValueOrDefault(t.CompanyId, ""),
            t.ClientId,
            users.GetValueOrDefault(t.ClientId, ""),
            t.AssignedToUserId.HasValue ? users.GetValueOrDefault(t.AssignedToUserId.Value) : null,
            t.CreatedAt, t.DueDateUtc, t.ClosedAtUtc,
            t.DepartmentId, t.DepartmentId.HasValue ? departments.GetValueOrDefault(t.DepartmentId.Value) : null,
            t.SubBranchId, t.SubBranchId.HasValue ? subBranches.GetValueOrDefault(t.SubBranchId.Value) : null
        )).ToList();

        return Ok(new PagedResult<TicketListItem>(items, page, pageSize, totalCount));
    }

    /// <summary>
    /// Counts for the "Tickets" nav dropdown: total plus New / Opened (Open+InProgress+Resolved) / Closed,
    /// scoped exactly like <see cref="List"/> so the numbers always match what clicking through shows.
    /// </summary>
    [HttpGet("counts")]
    public async Task<ActionResult<TicketCountsDto>> Counts()
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();
        query = _currentUser.Role switch
        {
            UserRole.Client => query.Where(t => t.ClientId == _currentUser.UserId),
            UserRole.Admin => query,
            _ => query.Where(t => t.DepartmentId == null || t.DepartmentId == _currentUser.DepartmentId)
        };

        var byStatus = await query.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        int CountOf(params TicketStatus[] wanted) => byStatus.Where(x => wanted.Contains(x.Key)).Sum(x => x.Count);

        var newCount = CountOf(TicketStatus.New);
        var openedCount = CountOf(TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved);
        var closedCount = CountOf(TicketStatus.Closed);

        var openedStatuses = new[] { TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved };
        var mineCount = _currentUser.UserId.HasValue
            ? await query.CountAsync(t => openedStatuses.Contains(t.Status) &&
                (t.AssignedToUserId == _currentUser.UserId.Value || t.Assignees.Any(a => a.UserId == _currentUser.UserId.Value)))
            : 0;

        return Ok(new TicketCountsDto(newCount + openedCount + closedCount, newCount, openedCount, closedCount, mineCount));
    }

    private static List<TicketStatus> ParseStatuses(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<TicketStatus>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<TicketStatus>(s, true, out var v) ? (TicketStatus?)v : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> GetById(Guid id)
    {
        var ticket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();

        return Ok(await BuildDetailDto(id));
    }

    [HttpPost("{id:guid}/open")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> OpenTicket(Guid id, OpenTicketRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();
        if (ticket.Status != TicketStatus.New || !TicketWorkflow.CanTransition(ticket.Status, TicketStatus.Open))
            return BadRequest(new { message = "Only new tickets can be opened." });

        // The client's branch pick is only a suggestion — any staff member (not just Admin) can correct it
        // here, since clients don't always know which branch actually owns their issue.
        var subBranchError = await ValidateSubBranchAsync(request.DepartmentId, request.SubBranchId);
        if (subBranchError is not null) return BadRequest(new { message = subBranchError });

        var assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId
            && u.Role == UserRole.Employee);
        if (assignee is null) return BadRequest(new { message = "Assignee must be a support agent or consultant." });
        if (assignee.DepartmentId != request.DepartmentId)
            return BadRequest(new { message = "Assignee must belong to the selected branch." });
        if (request.SubBranchId.HasValue && assignee.SubBranchId != request.SubBranchId)
            return BadRequest(new { message = "Assignee must belong to the selected sub-branch." });

        var openedAtUtc = DateTime.UtcNow;
        var (dueDateUtc, slaError) = await ResolveDueDateAsync(request.SlaPlanId, request.DueDateUtc, openedAtUtc);
        if (slaError is not null) return BadRequest(new { message = slaError });

        ticket.Source = request.Source;
        ticket.HelpTopicId = request.HelpTopicId;
        ticket.DepartmentId = request.DepartmentId;
        ticket.SubBranchId = request.SubBranchId;
        ticket.SlaPlanId = request.SlaPlanId;
        ticket.Priority = request.Priority;
        ticket.DueDateUtc = dueDateUtc;
        ticket.CategoryId = request.CategoryId;
        ticket.AssignedToUserId = request.AssignedToUserId;
        ticket.Status = TicketStatus.Open;
        ticket.OpenedByUserId = _currentUser.UserId;
        ticket.OpenedAtUtc = openedAtUtc;
        _db.TicketAssignees.Add(new TicketAssignee { TicketId = ticket.Id, UserId = assignee.Id });

        var client = await _db.Users.FirstAsync(u => u.Id == ticket.ClientId);
        var openedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = openedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket opened by {FullName(openedBy)} and assigned to {FullName(assignee)}.</p>"
        });

        _db.Notifications.Add(new Notification { UserId = assignee.Id, Type = NotificationType.TicketAssigned, Message = $"Ticket #{ticket.TicketNumber} assigned to you", TicketId = ticket.Id });
        _db.Notifications.Add(new Notification { UserId = client.Id, Type = NotificationType.TicketOpened, Message = $"Your ticket #{ticket.TicketNumber} has been opened", TicketId = ticket.Id });
        await _db.SaveChangesAsync();

        await _emailSender.SendAsync(client.Email!, $"Your ticket #{ticket.TicketNumber} has been opened", EmailTemplates.TicketOpened(client.FirstName, ticket.TicketNumber));
        await _emailSender.SendAsync(assignee.Email!, $"Ticket #{ticket.TicketNumber} assigned to you", EmailTemplates.TicketAssigned(assignee.FirstName, ticket.TicketNumber, ticket.Title));
        await _notificationPublisher.PushToUserAsync(assignee.Id, "ticketAssigned", new { ticket.Id, ticket.TicketNumber, ticket.Title });
        await _notificationPublisher.PushToUserAsync(client.Id, "ticketOpened", new { ticket.Id, ticket.TicketNumber, ticket.Title });

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>Moves an already-opened ticket to a different branch. Only staff with access to the ticket's current branch (or Admin) may do this. Notifies everyone in the destination branch.</summary>
    [HttpPost("{id:guid}/transfer-branch")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> TransferBranch(Guid id, TransferTicketBranchRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();
        if (ticket.Status == TicketStatus.New) return BadRequest(new { message = "Open the ticket before transferring it to a branch." });
        // A closed ticket is history: transferring it would wipe its assignee record and notify a branch about work that is finished.
        if (ticket.Status == TicketStatus.Closed) return BadRequest(new { message = "A closed ticket cannot be transferred. Reopen it first." });

        var fromDepartment = ticket.DepartmentId.HasValue
            ? await _db.Departments.FirstOrDefaultAsync(d => d.Id == ticket.DepartmentId.Value)
            : null;
        var toDepartment = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.IsActive);
        if (toDepartment is null) return BadRequest(new { message = "Destination branch not found." });
        if (toDepartment.Id == ticket.DepartmentId) return BadRequest(new { message = "Ticket is already in this branch." });

        var transferredBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);

        ticket.DepartmentId = toDepartment.Id;
        ticket.SubBranchId = null; // the sub-branch belonged to the old branch; staff can re-pick one for the new branch via the assignees/open flow
        ticket.AssignedToUserId = null; // the previous assignee(s) likely don't belong to the destination branch
        var oldAssignees = await _db.TicketAssignees.Where(a => a.TicketId == ticket.Id).ToListAsync();
        _db.TicketAssignees.RemoveRange(oldAssignees);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = transferredBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket transferred from <b>{H(fromDepartment?.Name ?? "(unrouted)")}</b> to <b>{H(toDepartment.Name)}</b> by {FullName(transferredBy)}.</p>"
        });
        await _db.SaveChangesAsync();

        var destinationStaff = await _db.Users
            .Where(u => u.Role == UserRole.Employee && u.DepartmentId == toDepartment.Id && u.IsActive)
            .ToListAsync();

        foreach (var member in destinationStaff)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = member.Id,
                Type = NotificationType.TicketTransferred,
                Message = $"Ticket #{ticket.TicketNumber} transferred to {toDepartment.Name}",
                TicketId = ticket.Id
            });
        }
        await _db.SaveChangesAsync();

        foreach (var member in destinationStaff)
        {
            await _emailSender.SendAsync(member.Email!, $"Ticket #{ticket.TicketNumber} transferred to your branch",
                EmailTemplates.TicketTransferred(member.FirstName, ticket.TicketNumber, ticket.Title,
                    fromDepartment?.Name ?? "(unrouted)", toDepartment.Name, $"{transferredBy.FirstName} {transferredBy.LastName}"));
            await _notificationPublisher.PushToUserAsync(member.Id, "ticketTransferred", new { ticket.Id, ticket.TicketNumber, ticket.Title, Branch = toDepartment.Name });
        }

        return Ok(await BuildDetailDto(id));
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> CloseTicket(Guid id, CloseTicketRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();
        if (ticket.Status is TicketStatus.New) return BadRequest(new { message = "Ticket must be opened before it can be closed." });
        if (ticket.Status == TicketStatus.Closed) return BadRequest(new { message = "Ticket is already closed." });
        if (!TicketWorkflow.CanTransition(ticket.Status, TicketStatus.Closed))
            return BadRequest(new { message = $"A {ticket.Status} ticket cannot be closed." });
        if (string.IsNullOrWhiteSpace(request.ResolutionSummary))
            return BadRequest(new { message = "A resolution summary is required to close a ticket." });

        ticket.Status = TicketStatus.Closed;
        ticket.ResolutionSummary = request.ResolutionSummary;
        ticket.ClosedByUserId = _currentUser.UserId;
        ticket.ClosedAtUtc = DateTime.UtcNow;

        var client = await _db.Users.FirstAsync(u => u.Id == ticket.ClientId);
        var closedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);

        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = closedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket closed by {FullName(closedBy)}.</p>"
        });

        _db.Notifications.Add(new Notification { UserId = client.Id, Type = NotificationType.TicketClosed, Message = $"Ticket #{ticket.TicketNumber} has been resolved", TicketId = ticket.Id });
        await _db.SaveChangesAsync();

        await _emailSender.SendAsync(client.Email!, $"Your ticket #{ticket.TicketNumber} has been resolved",
            EmailTemplates.TicketClosed(client.FirstName, ticket.TicketNumber, $"{closedBy.FirstName} {closedBy.LastName}"));
        await _notificationPublisher.PushToUserAsync(client.Id, "ticketClosed", new { ticket.Id, ticket.TicketNumber });

        // Embedding calls an external API; the ticket is already closed, so a failure here must not report the close as failed.
        try
        {
            await _knowledgeBaseIndexer.IndexTicketAsync(ticket.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Indexing closed ticket {TicketId} for the knowledge base failed", ticket.Id);
        }

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>
    /// Moves a live ticket between Open, InProgress and Resolved. Closing needs a resolution summary and reopening
    /// a closed ticket is its own action, so neither goes through here. Which moves are legal is decided in
    /// <see cref="TicketWorkflow"/>.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> ChangeStatus(Guid id, ChangeTicketStatusRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();

        if (!TicketWorkflow.IsDirectlyAssignable(request.Status))
            return BadRequest(new { message = $"A ticket cannot be moved to {request.Status} this way." });
        if (ticket.Status == request.Status)
            return BadRequest(new { message = $"The ticket is already {request.Status}." });
        if (!TicketWorkflow.CanTransition(ticket.Status, request.Status))
            return BadRequest(new { message = $"A {ticket.Status} ticket cannot be moved to {request.Status}." });

        var from = ticket.Status;
        ticket.Status = request.Status;

        var changedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = changedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Status changed from <b>{H(from.ToString())}</b> to <b>{H(request.Status.ToString())}</b> by {FullName(changedBy)}.</p>"
        });
        await _db.SaveChangesAsync();

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>The ticket's due date: what staff typed if they typed one, otherwise the SLA plan's resolution time counted from <paramref name="startUtc"/>.</summary>
    private async Task<(DateTime? DueDateUtc, string? Error)> ResolveDueDateAsync(Guid slaPlanId, DateTime? requested, DateTime startUtc)
    {
        var plan = await _db.SlaPlans.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slaPlanId && s.IsActive);
        if (plan is null) return (null, "SLA plan not found.");

        if (requested.HasValue)
        {
            var requestedUtc = requested.Value.ToUniversalTime();
            if (requestedUtc <= startUtc) return (null, "The due date must be in the future.");
            return (requestedUtc, null);
        }

        return (_sla.ComputeDueDateUtc(startUtc, plan.ResolutionTimeHours), null);
    }

    /// <summary>
    /// Reopens a closed ticket — e.g. something was missed and needs to be added or fixed before it's
    /// really done. Any Admin or Employee with access to the ticket can reopen it, not just whoever closed it.
    /// </summary>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> ReopenTicket(Guid id)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();
        if (ticket.Status != TicketStatus.Closed) return BadRequest(new { message = "Only closed tickets can be reopened." });

        ticket.Status = TicketStatus.Open;
        ticket.ClosedByUserId = null;
        ticket.ClosedAtUtc = null;
        // The ticket is live again: if it is (still) past its due date the overdue alert has to be able to fire again.
        ticket.OverdueNotifiedAtUtc = null;

        var reopenedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = reopenedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket reopened by {FullName(reopenedBy)}.</p>"
        });

        await _db.SaveChangesAsync();

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>Adds a co-assignee — e.g. a second person who helped resolve a different part of the same ticket.</summary>
    [HttpPost("{id:guid}/assignees")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> AddAssignee(Guid id, AddTicketAssigneeRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();
        if (ticket.Status is TicketStatus.New or TicketStatus.Closed)
            return BadRequest(new { message = "The ticket must be open to change its assignees." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId && u.Role == UserRole.Employee);
        if (user is null) return BadRequest(new { message = "Assignee must be a support agent or consultant." });
        if (user.DepartmentId != ticket.DepartmentId) return BadRequest(new { message = "Assignee must belong to this ticket's branch." });
        if (ticket.SubBranchId.HasValue && user.SubBranchId != ticket.SubBranchId)
            return BadRequest(new { message = "Assignee must belong to this ticket's sub-branch." });

        var alreadyAssigned = await _db.TicketAssignees.AnyAsync(a => a.TicketId == id && a.UserId == request.UserId);
        if (alreadyAssigned) return BadRequest(new { message = "This person is already assigned to the ticket." });

        _db.TicketAssignees.Add(new TicketAssignee { TicketId = id, UserId = user.Id });

        var addedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = id,
            AuthorId = addedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>{FullName(user)} added as an assignee by {FullName(addedBy)}.</p>"
        });

        _db.Notifications.Add(new Notification { UserId = user.Id, Type = NotificationType.TicketAssigned, Message = $"Ticket #{ticket.TicketNumber} assigned to you", TicketId = ticket.Id });
        await _db.SaveChangesAsync();

        await _emailSender.SendAsync(user.Email!, $"Ticket #{ticket.TicketNumber} assigned to you", EmailTemplates.TicketAssigned(user.FirstName, ticket.TicketNumber, ticket.Title));
        await _notificationPublisher.PushToUserAsync(user.Id, "ticketAssigned", new { ticket.Id, ticket.TicketNumber, ticket.Title });

        return Ok(await BuildDetailDto(id));
    }

    [HttpDelete("{id:guid}/assignees/{userId:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> RemoveAssignee(Guid id, Guid userId)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();

        var assignment = await _db.TicketAssignees.FirstOrDefaultAsync(a => a.TicketId == id && a.UserId == userId);
        if (assignment is null) return NotFound();

        _db.TicketAssignees.Remove(assignment);
        if (ticket.AssignedToUserId == userId)
        {
            var remaining = await _db.TicketAssignees.Where(a => a.TicketId == id && a.UserId != userId).FirstOrDefaultAsync();
            ticket.AssignedToUserId = remaining?.UserId;
        }
        await _db.SaveChangesAsync();

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>Manually logs when work on the ticket started/ended — not tied to any single assignee.</summary>
    [HttpPatch("{id:guid}/work-time")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> SetWorkTime(Guid id, SetTicketWorkTimeRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();
        if (request.StartedAtUtc.HasValue && request.EndedAtUtc.HasValue && request.EndedAtUtc < request.StartedAtUtc)
            return BadRequest(new { message = "End time cannot be before start time." });

        ticket.WorkStartedAtUtc = request.StartedAtUtc;
        ticket.WorkEndedAtUtc = request.EndedAtUtc;
        await _db.SaveChangesAsync();

        return Ok(await BuildDetailDto(id));
    }

    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<TicketMessageDto>> AddMessage(Guid id, AddTicketMessageRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();

        if (request.Type == MessageType.SystemEvent)
            return BadRequest(new { message = "System events are recorded automatically and cannot be posted directly." });
        if (_currentUser.Role == UserRole.Client && request.Type != MessageType.Response)
            return Forbid();

        var message = new TicketMessage
        {
            TicketId = id,
            AuthorId = _currentUser.UserId!.Value,
            Type = request.Type,
            BodyHtml = _html.Sanitize(request.BodyHtml)
        };
        _db.TicketMessages.Add(message);
        await _db.SaveChangesAsync();

        if (request.Type == MessageType.Response)
        {
            var recipientId = _currentUser.Role == UserRole.Client ? ticket.AssignedToUserId : ticket.ClientId;
            if (recipientId.HasValue)
            {
                var recipient = await _db.Users.FirstOrDefaultAsync(u => u.Id == recipientId.Value);
                if (recipient is not null)
                {
                    _db.Notifications.Add(new Notification { UserId = recipient.Id, Type = NotificationType.NewResponse, Message = $"New reply on ticket #{ticket.TicketNumber}", TicketId = ticket.Id });
                    await _db.SaveChangesAsync();
                    await _emailSender.SendAsync(recipient.Email!, $"New reply on ticket #{ticket.TicketNumber}", EmailTemplates.NewResponse(recipient.FirstName, ticket.TicketNumber));
                    await _notificationPublisher.PushToUserAsync(recipient.Id, "newResponse", new { ticket.Id, ticket.TicketNumber });
                }
            }
        }

        if (request.Type == MessageType.InternalNote)
        {
            await _knowledgeBaseIndexer.IndexTicketAsync(ticket.Id);
        }

        var author = await _db.Users.FirstAsync(u => u.Id == message.AuthorId);
        return Ok(new TicketMessageDto(message.Id, message.Type.ToString(), message.BodyHtml, message.AuthorId,
            $"{author.FirstName} {author.LastName}", message.CreatedAt, new List<TicketAttachmentDto>()));
    }

    /// <summary>A branch with sub-branches requires picking exactly one of them; a branch without any must not receive one.</summary>
    private async Task<string?> ValidateSubBranchAsync(Guid departmentId, Guid? subBranchId)
    {
        var activeSubBranches = await _db.SubBranches.Where(s => s.DepartmentId == departmentId && s.IsActive).ToListAsync();
        if (activeSubBranches.Count == 0)
            return subBranchId.HasValue ? "This branch has no sub-branches." : null;

        if (subBranchId is null) return "A sub-branch must be selected for this branch.";
        if (!activeSubBranches.Any(s => s.Id == subBranchId.Value)) return "Sub-branch not found for this branch.";
        return null;
    }

    private bool CanAccess(Ticket ticket) => _currentUser.Role switch
    {
        UserRole.Admin => true,
        // Branches are isolated: staff can access unrouted (New) tickets for triage, plus tickets routed to their own branch.
        UserRole.Employee => ticket.DepartmentId == null || ticket.DepartmentId == _currentUser.DepartmentId,
        UserRole.Client => ticket.ClientId == _currentUser.UserId,
        _ => false
    };

    /// <summary>The link handed to the browser is a signed, expiring download URL — never the raw storage path.</summary>
    private TicketAttachmentDto ToAttachmentDto(TicketAttachment a) =>
        new(a.Id, a.FileName, _links.ToDownloadUrl(a.FileUrl) ?? string.Empty, a.FileSizeBytes, a.ContentType, a.CreatedAt);

    private async Task<TicketDetailDto> BuildDetailDto(Guid ticketId)
    {
        var t = await _db.Tickets.AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.HelpTopic)
            .Include(x => x.Category)
            .Include(x => x.Department)
            .Include(x => x.SubBranch)
            .Include(x => x.SlaPlan)
            .Include(x => x.Attachments)
            .FirstAsync(x => x.Id == ticketId);

        var messages = await _db.TicketMessages.AsNoTracking()
            .Include(m => m.Attachments)
            .Where(m => m.TicketId == ticketId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        // Clients never see InternalNote or SystemEvent entries — only the Response conversation.
        if (_currentUser.Role == UserRole.Client)
            messages = messages.Where(m => m.Type == MessageType.Response).ToList();

        var assignees = await _db.TicketAssignees.AsNoTracking().Where(a => a.TicketId == ticketId).ToListAsync();

        var relevantUserIds = new List<Guid> { t.ClientId };
        if (t.AssignedToUserId.HasValue) relevantUserIds.Add(t.AssignedToUserId.Value);
        relevantUserIds.AddRange(assignees.Select(a => a.UserId));
        relevantUserIds.AddRange(messages.Select(m => m.AuthorId));
        var users = await _db.Users.Where(u => relevantUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        string NameOf(Guid userId) => users.TryGetValue(userId, out var u) ? $"{u.FirstName} {u.LastName}" : "";

        var messageDtos = messages.Select(m => new TicketMessageDto(
            m.Id, m.Type.ToString(), _html.Sanitize(m.BodyHtml), m.AuthorId, NameOf(m.AuthorId), m.CreatedAt,
            m.Attachments.Select(ToAttachmentDto).ToList()
        )).ToList();

        var ticketLevelAttachments = t.Attachments.Where(a => a.MessageId == null).Select(ToAttachmentDto).ToList();

        var assigneeDtos = assignees.Select(a => new TicketAssigneeDto(a.UserId, NameOf(a.UserId))).ToList();

        users.TryGetValue(t.ClientId, out var clientUser);

        return new TicketDetailDto(
            t.Id, t.TicketNumber, t.Title, _html.Sanitize(t.Description), t.Status.ToString(),
            t.ClientId, NameOf(t.ClientId), clientUser?.Email, clientUser?.PhoneNumber,
            t.CompanyId, t.Company?.Name ?? "",
            t.Source?.ToString(), t.HelpTopicId, t.HelpTopic?.Name, t.DepartmentId, t.Department?.Name,
            t.SubBranchId, t.SubBranch?.Name,
            _currentUser.Role == UserRole.Client ? null : t.SlaPlanId,
            _currentUser.Role == UserRole.Client ? null : t.SlaPlan?.Name,
            t.Priority?.ToString(), t.DueDateUtc,
            _currentUser.Role == UserRole.Client ? null : t.CategoryId,
            _currentUser.Role == UserRole.Client ? null : t.Category?.Name,
            t.AssignedToUserId,
            t.AssignedToUserId.HasValue ? NameOf(t.AssignedToUserId.Value) : null,
            assigneeDtos, t.WorkStartedAtUtc, t.WorkEndedAtUtc,
            t.OpenedAtUtc, t.ClosedAtUtc, t.ResolutionSummary, t.CreatedAt,
            ticketLevelAttachments, messageDtos
        );
    }
}
