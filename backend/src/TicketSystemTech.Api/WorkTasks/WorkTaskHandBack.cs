using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.WorkTasks;

/// <summary>
/// What happens to the unfinished tasks of a person who stops working (deactivated, moved to another branch or role, anonymised). A task
/// left on them would sit there "in progress" for ever, with nobody told and nobody able to see it needed anyone. Tickets are released
/// from such a person; a task has to belong to somebody, so it goes back to the colleague who handed it over (or, failing that, to the one
/// who made it) as a task that has not been started, with the usual notice and a line in its hand-over history.
/// </summary>
public static class WorkTaskHandBack
{
    /// <summary>
    /// Hands the unfinished tasks of <paramref name="personId"/> back and stages everything that belongs to it in <paramref name="db"/>
    /// (the caller saves): the new holder, the history line, the audit row and the list line and e-mail. Returns the events whose
    /// pushes are to be sent after the save. A task nobody active in its branch can take stays where it is. The person's running timers are
    /// ended separately (<see cref="WorkSessionExtensions.EndRunningTaskSessionsAsync"/>).
    /// </summary>
    public static async Task<IReadOnlyList<TaskAssignedEvent>> HandBackOpenTasksAsync(
        this AppDbContext db, INotifier notifier, Guid personId, Guid? actingUserId, string reason, CancellationToken ct = default)
    {
        if (actingUserId is not { } by) return Array.Empty<TaskAssignedEvent>(); // always somebody in a request; a task is never handed back by nobody
        var tasks = await db.WorkTasks
            .Where(t => t.AssignedToUserId == personId && t.Status != WorkTaskStatus.Done)
            .ToListAsync(ct);
        if (tasks.Count == 0) return Array.Empty<TaskAssignedEvent>();

        var acting = await db.Users.FirstOrDefaultAsync(u => u.Id == by, ct);
        var events = new List<TaskAssignedEvent>();

        foreach (var task in tasks)
        {
            var candidateIds = new[] { task.AssignedByUserId, task.CreatedByUserId }.Where(id => id != personId).Distinct().ToList();
            var candidates = await db.Users
                .Where(u => candidateIds.Contains(u.Id) && u.IsActive && u.Role == UserRole.Employee && u.DepartmentId == task.DepartmentId)
                .ToListAsync(ct);
            var next = candidateIds.Select(id => candidates.FirstOrDefault(c => c.Id == id)).FirstOrDefault(c => c is not null);
            if (next is null) continue;

            task.AssignedToUserId = next.Id;
            task.AssignedByUserId = by;
            task.Status = WorkTaskStatus.Pending; // nobody is working on it now, whatever it was before
            db.WorkTaskAssignmentLogs.Add(new WorkTaskAssignmentLog { WorkTaskId = task.Id, AssignedByUserId = by, AssignedToUserId = next.Id });
            db.Audit(by, "WorkTaskHandedBack", nameof(WorkTask), task.Id, $"From {personId} to {next.Id}: {reason}");

            if (acting is null) continue;
            var assigned = new TaskAssignedEvent(task, next, acting);
            db.Notifications.AddRange(notifier.InApp(assigned));
            db.EmailOutbox.AddRange(notifier.Emails(assigned));
            events.Add(assigned);
        }

        return events;
    }
}
