using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Notifications;
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

/// <summary>Replies and internal notes on a ticket.</summary>
public interface ITicketMessageService
{
    /// <param name="rawKey">The Idempotency-Key header as it came, or null when there was none.</param>
    Task<Outcome<TicketMessageDto>> AddAsync(Guid id, AddTicketMessageRequest request, string? rawKey);
}

public sealed class TicketMessageService : ITicketMessageService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly INotifier _notifier;
    private readonly IKnowledgeBaseIndexQueue _indexQueue;
    private readonly IHtmlSanitizerService _html;
    private readonly ITicketQueries _queries;
    private readonly ITicketIdempotency _idempotency;

    public TicketMessageService(
        AppDbContext db, ICurrentUserService currentUser, INotifier notifier,
        IKnowledgeBaseIndexQueue indexQueue, IHtmlSanitizerService html, ITicketQueries queries, ITicketIdempotency idempotency)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier;
        _indexQueue = indexQueue;
        _html = html;
        _queries = queries;
        _idempotency = idempotency;
    }

    public async Task<Outcome<TicketMessageDto>> AddAsync(Guid id, AddTicketMessageRequest request, string? rawKey)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketMessageDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketMessageDto>.Forbidden();

        if (request.Type == MessageType.SystemEvent)
            return Outcome<TicketMessageDto>.Invalid("System events are recorded automatically and cannot be posted directly.");
        if (HtmlText.IsBlank(request.BodyHtml))
            return Outcome<TicketMessageDto>.Invalid("The message is empty.");
        if (_currentUser.Role == UserRole.Client && request.Type != MessageType.Response)
            return Outcome<TicketMessageDto>.Forbidden();
        // The screens already hide the reply box on a closed ticket; the API now enforces it too.
        if (ticket.Status == TicketStatus.Closed)
            return Outcome<TicketMessageDto>.Invalid("This ticket is closed. Reopen it to add a message.");

        // The same message sent twice (a double click, a retry after a timeout) must be one message: a request that carries
        // an Idempotency-Key the server has already seen gets the first answer back instead of a second copy.
        var authorId = _currentUser.UserId!.Value;
        var (idempotencyKey, keyError) = TicketIdempotency.Check(rawKey);
        if (keyError is not null) return Outcome<TicketMessageDto>.Invalid(keyError);
        if (idempotencyKey is not null && await _idempotency.FindPreviousAsync(authorId, idempotencyKey, TicketIdempotency.MessageScope) is { } earlierId)
            return Outcome<TicketMessageDto>.Ok(await _queries.BuildMessageAsync(earlierId));

        var message = new TicketMessage
        {
            TicketId = id,
            AuthorId = authorId,
            Type = request.Type,
            BodyHtml = _html.Sanitize(request.BodyHtml)
        };
        _db.TicketMessages.Add(message);

        // The notice (the list line and the e-mail) is staged BEFORE the one save, so that the reply and what tells the other side about it stand or
        // fall together: a reply that was saved while its notice was lost would be answered by the same Idempotency-Key with "done" and no mail.
        NewResponseEvent? reply = null;
        if (request.Type == MessageType.Response)
        {
            // A client's reply goes to everyone working on the ticket, not just the primary assignee; a staff reply goes to the client.
            var recipientIds = _currentUser.Role == UserRole.Client
                ? await StaffToNotifyAsync(ticket)
                : new List<Guid> { ticket.ClientId };

            var recipients = await _db.Users.Where(u => recipientIds.Contains(u.Id) && u.IsActive).ToListAsync();
            reply = new NewResponseEvent(ticket, recipients);
            _db.Notifications.AddRange(_notifier.InApp(reply));
            _db.EmailOutbox.AddRange(_notifier.Emails(reply));
        }

        var raced = await _idempotency.SaveAsync(authorId, idempotencyKey, message.Id, TicketIdempotency.MessageScope);
        if (raced is { } winner) return Outcome<TicketMessageDto>.Ok(await _queries.BuildMessageAsync(winner));

        if (reply is not null) await _notifier.DeliverAsync(reply); // the push, after the save

        // Only worth embedding once the ticket is past triage (search never returns New/Open tickets), and the
        // embedding call is an external dependency: its failure must not fail a message that is already saved.
        if (request.Type == MessageType.InternalNote && ticket.Status is not (TicketStatus.New or TicketStatus.Open))
            _indexQueue.Enqueue(ticket.Id);

        return Outcome<TicketMessageDto>.Ok(await _queries.BuildMessageAsync(message.Id));
    }

    /// <summary>Who should hear about a client's reply: every assignee. A ticket with nobody on it falls back to the staff of its
    /// branch, so a reply is never addressed to no one.</summary>
    private async Task<List<Guid>> StaffToNotifyAsync(Ticket ticket)
    {
        var ids = await _db.TicketAssignees.Where(a => a.TicketId == ticket.Id).Select(a => a.UserId).ToListAsync();

        if (ids.Count == 0 && ticket.DepartmentId.HasValue)
            ids = await _db.Users.Where(u => u.Role == UserRole.Employee && u.IsActive && u.DepartmentId == ticket.DepartmentId).Select(u => u.Id).ToListAsync();

        return ids;
    }
}
