using System.Net;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

public class TicketAssignmentService : ITicketAssignmentService
{
    private static readonly TicketStatus[] LiveStatuses = { TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved };

    private readonly AppDbContext _db;
    private readonly INotificationPublisher _publisher;

    public TicketAssignmentService(AppDbContext db, INotificationPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public async Task<int> ReleaseLiveTicketsAsync(Guid userId, string reason, Guid? actorUserId, CancellationToken ct = default)
    {
        var person = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (person is null) return 0;
        var personName = WebUtility.HtmlEncode($"{person.FirstName} {person.LastName}");

        var tickets = await _db.Tickets
            .Include(t => t.Assignees)
            .Where(t => LiveStatuses.Contains(t.Status) &&
                        (t.AssignedToUserId == userId || t.Assignees.Any(a => a.UserId == userId)))
            .ToListAsync(ct);
        if (tickets.Count == 0) return 0;

        // They stop working these tickets now: a session left running would keep counting after they have gone.
        await _db.EndRunningSessionsAsync(tickets.Select(t => t.Id).ToList(), userId, DateTime.UtcNow, ct);

        var orphaned = new List<Ticket>();
        foreach (var ticket in tickets)
        {
            foreach (var assignment in ticket.Assignees.Where(a => a.UserId == userId).ToList())
                _db.TicketAssignees.Remove(assignment);

            if (ticket.AssignedToUserId == userId)
            {
                // The primary assignee is the first of the others still on the ticket, if there is one.
                ticket.AssignedToUserId = ticket.Assignees
                    .Where(a => a.UserId != userId)
                    .OrderBy(a => a.AssignedAtUtc)
                    .Select(a => (Guid?)a.UserId)
                    .FirstOrDefault();
            }

            var nobodyLeft = !ticket.Assignees.Any(a => a.UserId != userId);
            if (nobodyLeft) orphaned.Add(ticket);

            _db.TicketMessages.Add(new TicketMessage
            {
                TicketId = ticket.Id,
                AuthorId = actorUserId ?? userId,
                Type = MessageType.SystemEvent,
                BodyHtml = $"<p>{personName} was unassigned from this ticket ({WebUtility.HtmlEncode(reason)})" +
                           (nobodyLeft ? "; it currently has no assignee." : ".") + "</p>"
            });
        }

        // A ticket nobody owns is exactly the kind that gets forgotten: tell the active staff of its branch.
        var notifyByBranch = new Dictionary<Guid, List<Guid>>();
        foreach (var ticket in orphaned.Where(t => t.DepartmentId.HasValue))
        {
            var branchId = ticket.DepartmentId!.Value;
            if (!notifyByBranch.TryGetValue(branchId, out var staffIds))
            {
                staffIds = await _db.Users
                    .Where(u => u.Role == UserRole.Employee && u.IsActive && u.DepartmentId == branchId && u.Id != userId)
                    .Select(u => u.Id)
                    .ToListAsync(ct);
                notifyByBranch[branchId] = staffIds;
            }

            foreach (var staffId in staffIds)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = staffId,
                    Type = NotificationType.TicketUnassigned,
                    Message = $"Ticket #{ticket.TicketNumber} has no assignee",
                    TicketId = ticket.Id
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        foreach (var ticket in orphaned.Where(t => t.DepartmentId.HasValue))
            foreach (var staffId in notifyByBranch[ticket.DepartmentId!.Value])
                await _publisher.PushToUserAsync(staffId, "ticketUnassigned", new { ticket.Id, ticket.TicketNumber, ticket.Title }, ct);

        return tickets.Count;
    }
}
