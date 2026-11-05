using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.WorkTime;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;
using static TicketSystemTech.Api.Tickets.TicketHelpers;

namespace TicketSystemTech.Api.Tickets;

/// <summary>Who is on a ticket. The first person added becomes the one it is "assigned to"; everybody after is help.</summary>
public interface ITicketAssigneeService
{
    /// <summary>Adds a co-assignee — e.g. a second person who helped resolve a different part of the same ticket.</summary>
    Task<Outcome<TicketDetailDto>> AddAsync(Guid id, Guid userId);

    Task<Outcome<TicketDetailDto>> RemoveAsync(Guid id, Guid userId);
}

public sealed class TicketAssigneeService : ITicketAssigneeService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailSender _emailSender;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ITicketQueries _queries;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public TicketAssigneeService(
        AppDbContext db, ICurrentUserService currentUser, IEmailSender emailSender, INotificationPublisher notificationPublisher,
        ITicketQueries queries, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _emailSender = emailSender;
        _notificationPublisher = notificationPublisher;
        _queries = queries;
        _time = time;
    }

    public async Task<Outcome<TicketDetailDto>> AddAsync(Guid id, Guid userId)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();
        if (ticket.Status is TicketStatus.New or TicketStatus.Closed)
            return Outcome<TicketDetailDto>.Invalid("The ticket must be open to change its assignees.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.Employee && u.IsActive);
        if (user is null) return Outcome<TicketDetailDto>.Invalid(TicketAssignment.NotAnAgent);
        var assigneeError = TicketAssignment.Check(user.DepartmentId, user.SubBranchId, ticket.DepartmentId, ticket.SubBranchId);
        if (assigneeError is not null) return Outcome<TicketDetailDto>.Invalid(assigneeError);

        var alreadyAssigned = await _db.TicketAssignees.AnyAsync(a => a.TicketId == id && a.UserId == userId);
        if (alreadyAssigned) return Outcome<TicketDetailDto>.Invalid("This person is already assigned to the ticket.");

        // The first person added to a ticket that has nobody becomes the one it is "assigned to"; everybody after is help.
        var hasPrimary = await _db.TicketAssignees.AnyAsync(a => a.TicketId == id && a.IsPrimary);
        _db.TicketAssignees.Add(new TicketAssignee { TicketId = id, UserId = user.Id, IsPrimary = !hasPrimary });

        var addedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);
        _db.TicketMessages.Add(new TicketMessage
        {
            TicketId = id,
            AuthorId = addedBy.Id,
            Type = MessageType.SystemEvent,
            BodyHtml = $"<p>{FullName(user)} added as an assignee by {FullName(addedBy)}.</p>"
        });

        _db.Notifications.Add(new Notification { UserId = user.Id, Type = NotificationType.TicketAssigned, Message = $"Ticket #{ticket.TicketNumber} assigned to you", TicketId = ticket.Id });
        _db.Audit(_currentUser.UserId, "TicketAssigneeAdded", nameof(Ticket), ticket.Id, $"Added {user.Id}");
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two identical requests (a double click) raced past the check above; the unique index stopped the second.
            // Anything else that made the save fail is a real error.
            if (await AssigneeExistsAsync(id, user.Id))
                return Outcome<TicketDetailDto>.Invalid("This person is already assigned to the ticket.");
            // Two people were added as the first assignee at the same moment; the unique index lets only one be the main one.
            if (!hasPrimary)
                return Outcome<TicketDetailDto>.Conflict("Somebody else changed the assignees of this ticket a moment ago. Reload it and try again.");
            throw;
        }

        await _emailSender.SendAsync(user.Email!, $"Ticket #{ticket.TicketNumber} assigned to you", EmailTemplates.TicketAssigned(user.FirstName, ticket.TicketNumber, ticket.Title));
        await _notificationPublisher.PushToUserAsync(user.Id, "ticketAssigned", new { ticket.Id, ticket.TicketNumber, ticket.Title });

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<TicketDetailDto>> RemoveAsync(Guid id, Guid userId)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();
        if (ticket.Status is TicketStatus.New or TicketStatus.Closed)
            return Outcome<TicketDetailDto>.Invalid("The ticket must be open to change its assignees."); // who worked a closed ticket is history

        var assignment = await _db.TicketAssignees.FirstOrDefaultAsync(a => a.TicketId == id && a.UserId == userId);
        if (assignment is null) return Outcome<TicketDetailDto>.NotFound();

        _db.TicketAssignees.Remove(assignment);
        _db.Audit(_currentUser.UserId, "TicketAssigneeRemoved", nameof(Ticket), ticket.Id, $"Removed {userId}");
        await _db.EndRunningSessionsAsync(new[] { id }, userId, UtcNow); // the person stops working it
        if (assignment.IsPrimary)
        {
            // The one the ticket was assigned to leaves: whoever has been on it longest of the others takes over, if anyone is left.
            var next = await _db.TicketAssignees.Where(a => a.TicketId == id && a.UserId != userId).OrderBy(a => a.AssignedAtUtc).FirstOrDefaultAsync();
            if (next is not null) next.IsPrimary = true;
        }
        await _db.SaveChangesAsync();

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    private async Task<bool> AssigneeExistsAsync(Guid ticketId, Guid userId)
    {
        _db.ChangeTracker.Clear();
        return await _db.TicketAssignees.AnyAsync(a => a.TicketId == ticketId && a.UserId == userId);
    }
}
