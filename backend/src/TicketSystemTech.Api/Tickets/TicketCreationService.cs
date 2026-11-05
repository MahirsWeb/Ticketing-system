using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;
using static TicketSystemTech.Api.Tickets.TicketHelpers;

namespace TicketSystemTech.Api.Tickets;

/// <summary>New tickets: from a client, or from staff on behalf of a client who phoned.</summary>
public interface ITicketCreationService
{
    /// <param name="rawKey">The Idempotency-Key header as it came, or null when there was none.</param>
    Task<Outcome<TicketDetailDto>> CreateAsync(CreateTicketRequest request, string? rawKey);

    /// <summary>
    /// Staff creates a ticket on behalf of a client who reported the issue verbally (e.g. by phone) rather
    /// than through the client portal. The ticket is created and opened in one step, and appears exactly as
    /// if the client had submitted it themselves.
    /// </summary>
    Task<Outcome<TicketDetailDto>> CreateOnBehalfAsync(CreateTicketOnBehalfRequest request, string? rawKey);
}

public sealed class TicketCreationService : ITicketCreationService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITicketNumberGenerator _ticketNumberGenerator;
    private readonly IEmailSender _emailSender;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IPiiProtector _piiProtector;
    private readonly IHtmlSanitizerService _html;
    private readonly ITicketQueries _queries;
    private readonly ITicketRouting _routing;
    private readonly ITicketIdempotency _idempotency;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public TicketCreationService(
        AppDbContext db, ICurrentUserService currentUser, ITicketNumberGenerator ticketNumberGenerator, IEmailSender emailSender,
        INotificationPublisher notificationPublisher, IPiiProtector piiProtector, IHtmlSanitizerService html, ITicketQueries queries,
        ITicketRouting routing, ITicketIdempotency idempotency, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _ticketNumberGenerator = ticketNumberGenerator;
        _emailSender = emailSender;
        _notificationPublisher = notificationPublisher;
        _piiProtector = piiProtector;
        _html = html;
        _queries = queries;
        _routing = routing;
        _idempotency = idempotency;
        _time = time;
    }

    public async Task<Outcome<TicketDetailDto>> CreateAsync(CreateTicketRequest request, string? rawKey)
    {
        var clientId = _currentUser.UserId!.Value;

        var (idempotencyKey, keyError) = TicketIdempotency.Check(rawKey);
        if (keyError is not null) return Outcome<TicketDetailDto>.Invalid(keyError);
        if (idempotencyKey is not null && await _idempotency.FindPreviousAsync(clientId, idempotencyKey) is { } previous)
            return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(previous)); // a retry of a request that already succeeded

        var companyId = _currentUser.CompanyId;
        if (companyId is null)
            return Outcome<TicketDetailDto>.Invalid("Your account is not linked to a company.");

        if (HtmlText.IsBlank(request.Description)) return Outcome<TicketDetailDto>.Invalid("Please describe the problem.");

        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.IsActive);
        if (department is null) return Outcome<TicketDetailDto>.Invalid("Please select a valid branch.");

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
        var raced = await _idempotency.SaveAsync(clientId, idempotencyKey, ticket.Id);
        if (raced is { } winner) return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(winner));

        foreach (var member in staff)
        {
            await _emailSender.SendAsync(member.Email!, $"New ticket #{ticket.TicketNumber} submitted — {company.Name}",
                EmailTemplates.NewTicketSubmitted(member.FirstName, ticket.TicketNumber, ticket.Title, $"{client.FirstName} {client.LastName}", company.Name));
            await _notificationPublisher.PushToUserAsync(member.Id, "newTicket", new { ticket.Id, ticket.TicketNumber, ticket.Title });
        }

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(ticket.Id));
    }

    public async Task<Outcome<TicketDetailDto>> CreateOnBehalfAsync(CreateTicketOnBehalfRequest request, string? rawKey)
    {
        var (idempotencyKey, keyError) = TicketIdempotency.Check(rawKey);
        if (keyError is not null) return Outcome<TicketDetailDto>.Invalid(keyError);
        if (idempotencyKey is not null && await _idempotency.FindPreviousAsync(_currentUser.UserId!.Value, idempotencyKey) is { } previous)
            return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(previous));

        var clientEmailHash = _piiProtector.Hash(request.ClientEmail);
        var client = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == clientEmailHash && u.Role == UserRole.Client);
        if (client is null) return Outcome<TicketDetailDto>.NotFound("No client account found with that email.");
        if (client.CompanyId is null) return Outcome<TicketDetailDto>.Invalid("This client is not linked to a company.");

        // Non-admin staff can only route a ticket into their own branch; moving it elsewhere later requires an explicit transfer.
        if (_currentUser.Role != UserRole.Admin && request.DepartmentId != _currentUser.DepartmentId)
            return Outcome<TicketDetailDto>.Invalid("You can only open tickets into your own branch.");
        if (HtmlText.IsBlank(request.Description)) return Outcome<TicketDetailDto>.Invalid("Please describe the problem.");

        var lookupError = await _routing.ValidateLookupsAsync(request.HelpTopicId, request.CategoryId, request.DepartmentId);
        if (lookupError is not null) return Outcome<TicketDetailDto>.Invalid(lookupError);

        var subBranchError = await _routing.ValidateSubBranchAsync(request.DepartmentId, request.SubBranchId);
        if (subBranchError is not null) return Outcome<TicketDetailDto>.Invalid(subBranchError);

        var assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId
            && u.Role == UserRole.Employee && u.IsActive);
        if (assignee is null) return Outcome<TicketDetailDto>.Invalid(TicketAssignment.NotAnAgent);
        var assigneeError = TicketAssignment.Check(assignee.DepartmentId, assignee.SubBranchId, request.DepartmentId, request.SubBranchId);
        if (assigneeError is not null) return Outcome<TicketDetailDto>.Invalid(assigneeError);

        var openedAtUtc = UtcNow;
        var (dueDateUtc, slaError) = await _routing.ResolveDueDateAsync(request.SlaPlanId, request.DueDateUtc, openedAtUtc);
        if (slaError is not null) return Outcome<TicketDetailDto>.Invalid(slaError);

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
            OpenedByUserId = _currentUser.UserId,
            OpenedAtUtc = openedAtUtc
        };
        _db.Tickets.Add(ticket);
        _db.TicketAssignees.Add(new TicketAssignee { Ticket = ticket, UserId = assignee.Id, IsPrimary = true });

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

        var raced = await _idempotency.SaveAsync(_currentUser.UserId!.Value, idempotencyKey, ticket.Id);
        if (raced is { } winner) return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(winner));

        await _emailSender.SendAsync(client.Email!, $"Your ticket #{ticket.TicketNumber} has been opened", EmailTemplates.TicketOpened(client.FirstName, ticket.TicketNumber));
        await _emailSender.SendAsync(assignee.Email!, $"Ticket #{ticket.TicketNumber} assigned to you", EmailTemplates.TicketAssigned(assignee.FirstName, ticket.TicketNumber, ticket.Title));
        await _notificationPublisher.PushToUserAsync(assignee.Id, "ticketAssigned", new { ticket.Id, ticket.TicketNumber, ticket.Title });
        await _notificationPublisher.PushToUserAsync(client.Id, "ticketOpened", new { ticket.Id, ticket.TicketNumber, ticket.Title });

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(ticket.Id));
    }
}
