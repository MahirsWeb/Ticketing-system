using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Tickets;

/// <summary>What the ticket list asks for. Every field is optional; a client sees only their own tickets and an employee only their branch's whatever is asked.</summary>
public sealed record TicketListFilter(
    string? Status, Guid? CompanyId, Guid? ClientId, Guid? AssignedToUserId, Guid? DepartmentId, Guid? SubBranchId,
    string? Search, string? SortBy, string? SortDir, DateTime? CreatedFrom, DateTime? CreatedTo, bool AnsweredOnly, int Page, int PageSize);

/// <summary>Reading tickets: the list, the counts, one ticket in full. Nothing here changes anything.</summary>
public interface ITicketQueries
{
    Task<PagedResult<TicketListItem>> ListAsync(TicketListFilter filter, CancellationToken ct = default);

    Task<TicketCountsDto> CountsAsync(CancellationToken ct = default);

    /// <summary>One ticket, or why it cannot be shown to the current user.</summary>
    Task<Outcome<TicketDetailDto>> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>The ticket as the current user is shown it. The caller has already checked that they may.</summary>
    Task<TicketDetailDto> BuildDetailAsync(Guid ticketId, CancellationToken ct = default);

    /// <summary>The person the ticket is assigned to (the primary one on its list), or null when nobody is on it.</summary>
    Task<Guid?> PrimaryAssigneeIdAsync(Guid ticketId, CancellationToken ct = default);

    /// <summary>A message as it is returned right after it was written.</summary>
    Task<TicketMessageDto> BuildMessageAsync(Guid messageId, CancellationToken ct = default);
}

public sealed class TicketQueries : ITicketQueries
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IHtmlSanitizerService _html;
    private readonly IFileLinkService _links;

    public TicketQueries(AppDbContext db, ICurrentUserService currentUser, IHtmlSanitizerService html, IFileLinkService links)
    {
        _db = db;
        _currentUser = currentUser;
        _html = html;
        _links = links;
    }

    public async Task<PagedResult<TicketListItem>> ListAsync(TicketListFilter filter, CancellationToken ct = default)
    {
        var (status, companyId, clientId, assignedToUserId, departmentId, subBranchId, search, sortBy, sortDir, createdFrom, createdTo, answeredOnly, page, pageSize) = filter;
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Tickets.AsNoTracking().AsQueryable();

        query = _currentUser.Role switch
        {
            UserRole.Client => query.Where(t => t.ClientId == _currentUser.UserId),
            UserRole.Admin => query, // Admin sees everything across all branches
            // Branches are isolated, strictly: an employee sees only their own branch's tickets (a ticket without a branch is Admin's alone).
            _ => query.Where(t => t.DepartmentId == _currentUser.DepartmentId)
        };

        var statuses = ParseStatuses(status);
        if (statuses.Count > 0) query = query.Where(t => statuses.Contains(t.Status));
        if (companyId.HasValue) query = query.Where(t => t.CompanyId == companyId.Value);
        if (clientId.HasValue) query = query.Where(t => t.ClientId == clientId.Value);
        if (assignedToUserId.HasValue)
            query = query.Where(t => t.Assignees.Any(a => a.UserId == assignedToUserId.Value));
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
            var staffIds = await _db.Users.Where(u => u.Role != UserRole.Client).Select(u => u.Id).ToListAsync(ct);
            query = query.Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.New &&
                t.Messages.Any(m => m.Type == MessageType.Response && staffIds.Contains(m.AuthorId)));
        }

        var totalCount = await query.CountAsync(ct);

        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            // Numbers have different lengths ("99999", "100002"), so sorting the text puts 100002 before 99999: shorter first, then text.
            "ticketnumber" => desc
                ? query.OrderByDescending(t => t.TicketNumber.Length).ThenByDescending(t => t.TicketNumber)
                : query.OrderBy(t => t.TicketNumber.Length).ThenBy(t => t.TicketNumber),
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
                t.Id,
                t.TicketNumber,
                t.Title,
                t.Status,
                t.Priority,
                t.CompanyId,
                t.ClientId,
                AssignedToUserId = t.Assignees.Where(a => a.IsPrimary).Select(a => (Guid?)a.UserId).FirstOrDefault(),
                t.CreatedAt,
                t.DueDateUtc,
                t.ClosedAtUtc,
                t.DepartmentId,
                t.SubBranchId
            })
            .ToListAsync(ct);

        var userIds = page_.SelectMany(t => new[] { t.ClientId, t.AssignedToUserId }).Where(id => id.HasValue).Select(id => id!.Value)
            .Concat(page_.Select(t => t.ClientId)).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
        var companyIds = page_.Select(t => t.CompanyId).Distinct().ToList();
        var companies = await _db.Companies.Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var departmentIds = page_.Where(t => t.DepartmentId.HasValue).Select(t => t.DepartmentId!.Value).Distinct().ToList();
        var departments = await _db.Departments.Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var subBranchIds = page_.Where(t => t.SubBranchId.HasValue).Select(t => t.SubBranchId!.Value).Distinct().ToList();
        var subBranches = await _db.SubBranches.Where(s => subBranchIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

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

        return new PagedResult<TicketListItem>(items, page, pageSize, totalCount);
    }

    /// <summary>
    /// Counts for the "Tickets" nav dropdown: total plus New / Opened (Open+InProgress+Resolved) / Closed,
    /// scoped exactly like <see cref="ListAsync"/> so the numbers always match what clicking through shows.
    /// </summary>
    public async Task<TicketCountsDto> CountsAsync(CancellationToken ct = default)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();
        query = _currentUser.Role switch
        {
            UserRole.Client => query.Where(t => t.ClientId == _currentUser.UserId),
            UserRole.Admin => query,
            _ => query.Where(t => t.DepartmentId == _currentUser.DepartmentId)
        };

        var byStatus = await query.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountOf(params TicketStatus[] wanted) => byStatus.Where(x => wanted.Contains(x.Key)).Sum(x => x.Count);

        var newCount = CountOf(TicketStatus.New);
        var openedCount = CountOf(TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved);
        var closedCount = CountOf(TicketStatus.Closed);

        var openedStatuses = new[] { TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved };
        var mineCount = _currentUser.UserId.HasValue
            ? await query.CountAsync(t => openedStatuses.Contains(t.Status) && t.Assignees.Any(a => a.UserId == _currentUser.UserId.Value), ct)
            : 0;

        return new TicketCountsDto(newCount + openedCount + closedCount, newCount, openedCount, closedCount, mineCount);
    }

    private static List<TicketStatus> ParseStatuses(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<TicketStatus>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<TicketStatus>(s, true, out var v) ? (TicketStatus?)v : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
    }

    public async Task<Outcome<TicketDetailDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var ticket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();

        return Outcome<TicketDetailDto>.Ok(await BuildDetailAsync(id, ct));
    }

    public Task<Guid?> PrimaryAssigneeIdAsync(Guid ticketId, CancellationToken ct = default) =>
        _db.TicketAssignees.Where(a => a.TicketId == ticketId && a.IsPrimary).Select(a => (Guid?)a.UserId).FirstOrDefaultAsync(ct);

    public async Task<TicketMessageDto> BuildMessageAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = await _db.TicketMessages.AsNoTracking().FirstAsync(m => m.Id == messageId, ct);
        var author = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == message.AuthorId, ct);
        return new TicketMessageDto(message.Id, message.Type.ToString(), message.BodyHtml, message.AuthorId,
            $"{author.FirstName} {author.LastName}", message.CreatedAt, new List<TicketAttachmentDto>());
    }

    /// <summary>The link handed to the browser is a signed, expiring download URL — never the raw storage path.</summary>
    private TicketAttachmentDto ToAttachmentDto(TicketAttachment a) =>
        new(a.Id, a.FileName, _links.ToDownloadUrl(a.FileUrl) ?? string.Empty, a.FileSizeBytes, a.ContentType, a.CreatedAt);

    public async Task<TicketDetailDto> BuildDetailAsync(Guid ticketId, CancellationToken ct = default)
    {
        var t = await _db.Tickets.AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.HelpTopic)
            .Include(x => x.Category)
            .Include(x => x.Department)
            .Include(x => x.SubBranch)
            .Include(x => x.SlaPlan)
            .Include(x => x.Attachments)
            .FirstAsync(x => x.Id == ticketId, ct);

        var messages = await _db.TicketMessages.AsNoTracking()
            .Include(m => m.Attachments)
            .Where(m => m.TicketId == ticketId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // Clients never see InternalNote or SystemEvent entries — only the Response conversation.
        if (_currentUser.Role == UserRole.Client)
            messages = messages.Where(m => m.Type == MessageType.Response).ToList();

        var assignees = await _db.TicketAssignees.AsNoTracking().Where(a => a.TicketId == ticketId).ToListAsync(ct);

        // How long staff worked is internal: the client gets an empty list.
        var sessions = _currentUser.Role == UserRole.Client
            ? new List<TicketWorkSession>()
            : await _db.TicketWorkSessions.AsNoTracking().Where(s => s.TicketId == ticketId).OrderBy(s => s.StartedAtUtc).ToListAsync(ct);

        var primaryId = assignees.FirstOrDefault(a => a.IsPrimary)?.UserId;
        var relevantUserIds = new List<Guid> { t.ClientId };
        relevantUserIds.AddRange(sessions.Select(s => s.UserId));
        relevantUserIds.AddRange(assignees.Select(a => a.UserId));
        relevantUserIds.AddRange(messages.Select(m => m.AuthorId));
        var users = await _db.Users.Where(u => relevantUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u, ct);

        string NameOf(Guid userId) => users.TryGetValue(userId, out var u) ? $"{u.FirstName} {u.LastName}" : "";

        var messageDtos = messages.Select(m => new TicketMessageDto(
            m.Id, m.Type.ToString(), _html.Sanitize(m.BodyHtml), m.AuthorId, NameOf(m.AuthorId), m.CreatedAt,
            m.Attachments.Select(ToAttachmentDto).ToList()
        )).ToList();

        // A file attached to the ticket itself (not to a message) is whatever staff put there, internal papers included: a client sees only
        // what they uploaded themselves. Files on a message follow the message, and a client sees only the replies.
        var ticketLevelAttachments = t.Attachments
            .Where(a => a.MessageId == null && (_currentUser.Role != UserRole.Client || a.UploadedByUserId == _currentUser.UserId))
            .Select(ToAttachmentDto).ToList();

        var assigneeDtos = assignees.Select(a => new TicketAssigneeDto(a.UserId, NameOf(a.UserId))).ToList();
        var sessionDtos = sessions.Select(s => new TicketWorkSessionDto(s.Id, s.UserId, NameOf(s.UserId), s.StartedAtUtc, s.EndedAtUtc)).ToList();

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
            primaryId,
            primaryId.HasValue ? NameOf(primaryId.Value) : null,
            assigneeDtos, sessionDtos,
            t.OpenedAtUtc, t.ClosedAtUtc, t.ResolutionSummary, t.CreatedAt,
            ticketLevelAttachments, messageDtos
        );
    }
}
