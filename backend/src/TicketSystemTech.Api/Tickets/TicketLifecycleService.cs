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

/// <summary>A ticket's life: opened, moved between branches and statuses, closed, reopened. Which moves are legal is decided in <see cref="TicketWorkflow"/>.</summary>
public interface ITicketLifecycleService
{
    Task<Outcome<TicketDetailDto>> OpenAsync(Guid id, OpenTicketRequest request);

    /// <summary>Moves an already-opened ticket to a different branch. Only staff with access to the ticket's current branch (or Admin) may do this. Notifies everyone in the destination branch.</summary>
    Task<Outcome<TicketDetailDto>> TransferBranchAsync(Guid id, TransferTicketBranchRequest request);

    Task<Outcome<TicketDetailDto>> CloseAsync(Guid id, CloseTicketRequest request);

    /// <summary>
    /// Moves a live ticket between Open, InProgress and Resolved. Closing needs a resolution summary and reopening
    /// a closed ticket is its own action, so neither goes through here.
    /// </summary>
    Task<Outcome<TicketDetailDto>> ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request);

    /// <summary>
    /// Reopens a closed ticket — e.g. something was missed and needs to be added or fixed before it's
    /// really done. Any Admin or Employee with access to the ticket can reopen it, not just whoever closed it.
    /// </summary>
    Task<Outcome<TicketDetailDto>> ReopenAsync(Guid id);
}

public sealed class TicketLifecycleService : ITicketLifecycleService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly INotifier _notifier;
    private readonly IKnowledgeBaseIndexQueue _indexQueue;
    private readonly ITicketQueries _queries;
    private readonly ITicketRouting _routing;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public TicketLifecycleService(
        AppDbContext db, ICurrentUserService currentUser, INotifier notifier,
        IKnowledgeBaseIndexQueue indexQueue, ITicketQueries queries, ITicketRouting routing, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier;
        _indexQueue = indexQueue;
        _queries = queries;
        _routing = routing;
        _time = time;
    }

    public async Task<Outcome<TicketDetailDto>> OpenAsync(Guid id, OpenTicketRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();
        if (ticket.Status != TicketStatus.New || !TicketWorkflow.CanTransition(ticket.Status, TicketStatus.Open))
            return Outcome<TicketDetailDto>.Invalid("Only new tickets can be opened.");

        var lookupError = await _routing.ValidateLookupsAsync(request.HelpTopicId, request.CategoryId, request.DepartmentId);
        if (lookupError is not null) return Outcome<TicketDetailDto>.Invalid(lookupError);

        // The client's branch pick is only a suggestion, and it is corrected here by whoever opens the ticket: Admin, or staff of the
        // branch the ticket sits in. Staff of another branch cannot see it at all (that is the point of branches); if the pick was
        // wrong, the branch that received it moves it on with "transfer".
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

        ticket.Source = request.Source;
        ticket.HelpTopicId = request.HelpTopicId;
        ticket.DepartmentId = request.DepartmentId;
        ticket.SubBranchId = request.SubBranchId;
        ticket.SlaPlanId = request.SlaPlanId;
        ticket.Priority = request.Priority;
        ticket.DueDateUtc = dueDateUtc;
        ticket.CategoryId = request.CategoryId;
        ticket.Status = TicketStatus.Open;
        ticket.OpenedByUserId = _currentUser.UserId;
        ticket.OpenedAtUtc = openedAtUtc;
        _db.TicketAssignees.Add(new TicketAssignee { TicketId = ticket.Id, UserId = assignee.Id, IsPrimary = true });

        var client = await _db.Users.FirstAsync(u => u.Id == ticket.ClientId);
        var openedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = openedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket opened by {FullName(openedBy)} and assigned to {FullName(assignee)}.</p>"
        });

        var opened = new TicketOpenedEvent(ticket, client, assignee);
        _db.Notifications.AddRange(_notifier.InApp(opened));
        _db.Audit(_currentUser.UserId, "TicketOpened", nameof(Ticket), ticket.Id, $"Assigned to {assignee.Id}; branch {ticket.DepartmentId}");
        await _db.SaveChangesAsync();

        await _notifier.DeliverAsync(opened);

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<TicketDetailDto>> TransferBranchAsync(Guid id, TransferTicketBranchRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();
        if (ticket.Status == TicketStatus.New) return Outcome<TicketDetailDto>.Invalid("Open the ticket before transferring it to a branch.");
        // A closed ticket is history: transferring it would wipe its assignee record and notify a branch about work that is finished.
        if (ticket.Status == TicketStatus.Closed) return Outcome<TicketDetailDto>.Invalid("A closed ticket cannot be transferred. Reopen it first.");

        var fromDepartment = ticket.DepartmentId.HasValue
            ? await _db.Departments.FirstOrDefaultAsync(d => d.Id == ticket.DepartmentId.Value)
            : null;
        var toDepartment = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.IsActive);
        if (toDepartment is null) return Outcome<TicketDetailDto>.Invalid("Destination branch not found.");
        if (toDepartment.Id == ticket.DepartmentId) return Outcome<TicketDetailDto>.Invalid("Ticket is already in this branch.");
        // A branch that has sub-branches needs one chosen, as everywhere else a ticket is routed: leaving it empty put the ticket outside
        // every sub-branch filter and past the check that assignees belong to the ticket's sub-branch, with no way to set it afterwards.
        var subBranchError = await _routing.ValidateSubBranchAsync(toDepartment.Id, request.SubBranchId);
        if (subBranchError is not null) return Outcome<TicketDetailDto>.Invalid(subBranchError);

        var transferredBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);

        ticket.DepartmentId = toDepartment.Id;
        ticket.SubBranchId = request.SubBranchId; // the old one belonged to the old branch; this is the one chosen in the new branch (or none, if it has none)
        var oldAssignees = await _db.TicketAssignees.Where(a => a.TicketId == ticket.Id).ToListAsync();
        _db.TicketAssignees.RemoveRange(oldAssignees);
        await _db.EndRunningSessionsAsync(new[] { ticket.Id }, null, UtcNow); // nobody is working it any more
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = transferredBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket transferred from <b>{H(fromDepartment?.Name ?? "(unrouted)")}</b> to <b>{H(toDepartment.Name)}</b> by {FullName(transferredBy)}.</p>"
        });
        _db.Audit(_currentUser.UserId, "TicketTransferred", nameof(Ticket), ticket.Id, $"Branch {fromDepartment?.Id.ToString() ?? "-"} -> {toDepartment.Id}");
        await _db.SaveChangesAsync();

        var destinationStaff = await _db.Users
            .Where(u => u.Role == UserRole.Employee && u.DepartmentId == toDepartment.Id && u.IsActive)
            .ToListAsync();

        var transferred = new TicketTransferredEvent(ticket, fromDepartment?.Name ?? "(unrouted)", toDepartment.Name, transferredBy, destinationStaff);
        _db.Notifications.AddRange(_notifier.InApp(transferred));
        await _db.SaveChangesAsync();

        await _notifier.DeliverAsync(transferred);

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<TicketDetailDto>> CloseAsync(Guid id, CloseTicketRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();
        if (ticket.Status is TicketStatus.New) return Outcome<TicketDetailDto>.Invalid("Ticket must be opened before it can be closed.");
        if (ticket.Status == TicketStatus.Closed) return Outcome<TicketDetailDto>.Invalid("Ticket is already closed.");
        if (!TicketWorkflow.CanTransition(ticket.Status, TicketStatus.Closed))
            return Outcome<TicketDetailDto>.Invalid($"A {ticket.Status} ticket cannot be closed.");
        if (string.IsNullOrWhiteSpace(request.ResolutionSummary))
            return Outcome<TicketDetailDto>.Invalid("A resolution summary is required to close a ticket.");

        ticket.Status = TicketStatus.Closed;
        ticket.ResolutionSummary = request.ResolutionSummary;
        ticket.ClosedByUserId = _currentUser.UserId;
        ticket.ClosedAtUtc = UtcNow;
        await _db.EndRunningSessionsAsync(new[] { ticket.Id }, null, ticket.ClosedAtUtc.Value);

        var client = await _db.Users.FirstAsync(u => u.Id == ticket.ClientId);
        var closedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);

        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = closedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket closed by {FullName(closedBy)}.</p>"
        });

        var closedEvent = new TicketClosedEvent(ticket, client, closedBy);
        _db.Notifications.AddRange(_notifier.InApp(closedEvent));
        _db.Audit(_currentUser.UserId, "TicketClosed", nameof(Ticket), ticket.Id, $"Ticket #{ticket.TicketNumber}");
        await _db.SaveChangesAsync();

        await _notifier.DeliverAsync(closedEvent);

        // Embedding calls an external API that can take half a minute: the ticket is already closed, so the answer does not wait for
        // it. A background worker indexes the ticket (and a failure there is logged, never reported as a failed close).
        _indexQueue.Enqueue(ticket.Id);

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<TicketDetailDto>> ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();

        if (!TicketWorkflow.IsDirectlyAssignable(request.Status))
            return Outcome<TicketDetailDto>.Invalid($"A ticket cannot be moved to {request.Status} this way.");
        if (ticket.Status == request.Status)
            return Outcome<TicketDetailDto>.Invalid($"The ticket is already {request.Status}.");
        if (!TicketWorkflow.CanTransition(ticket.Status, request.Status))
            return Outcome<TicketDetailDto>.Invalid($"A {ticket.Status} ticket cannot be moved to {request.Status}.");

        var from = ticket.Status;
        ticket.Status = request.Status;
        // Leaving Closed through this endpoint is a reopen like any other: the "closed by / closed at" facts no longer hold.
        if (from == TicketStatus.Closed) ClearClosedState(ticket);
        _db.Audit(_currentUser.UserId, "TicketStatusChanged", nameof(Ticket), ticket.Id, $"{from} -> {request.Status}");

        var changedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = changedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Status changed from <b>{H(from.ToString())}</b> to <b>{H(request.Status.ToString())}</b> by {FullName(changedBy)}.</p>"
        });
        await _db.SaveChangesAsync();

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<TicketDetailDto>> ReopenAsync(Guid id)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();
        if (ticket.Status != TicketStatus.Closed) return Outcome<TicketDetailDto>.Invalid("Only closed tickets can be reopened.");

        ticket.Status = TicketStatus.Open;
        ClearClosedState(ticket);
        _db.Audit(_currentUser.UserId, "TicketReopened", nameof(Ticket), ticket.Id, $"Ticket #{ticket.TicketNumber}");

        var reopenedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = reopenedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>Ticket reopened by {FullName(reopenedBy)}.</p>"
        });

        await _db.SaveChangesAsync();

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    /// <summary>What stops being true when a closed ticket becomes live again. Every way out of Closed goes through here.</summary>
    private static void ClearClosedState(Ticket ticket)
    {
        ticket.ClosedByUserId = null;
        ticket.ClosedAtUtc = null;
        // The ticket is live again: if it is (still) past its due date the overdue alert has to be able to fire again.
        ticket.OverdueNotifiedAtUtc = null;
    }
}
