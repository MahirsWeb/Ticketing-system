using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Application.WorkTime;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Api.WorkTasks;

/// <summary>
/// Informal work items ("tasks") that staff hand to each other alongside ticket work. Employees create, assign and reassign
/// these among themselves within their own branch; Admin can also create one into any branch.
/// </summary>
public interface IWorkTaskService
{
    /// <summary>Staff hand tasks to a colleague in their own branch; Admin may create and assign one into any branch.</summary>
    Task<Outcome<WorkTaskDetailDto>> CreateAsync(CreateWorkTaskRequest request);

    /// <summary>Hands the task off to someone else in the same branch. Every handoff is recorded in the assignment history.</summary>
    Task<Outcome<WorkTaskDetailDto>> ReassignAsync(Guid id, ReassignWorkTaskRequest request);

    /// <summary>
    /// Manually corrects the time of the task's latest stretch of work, or enters one if the task has none yet. Earlier
    /// stretches are left alone. Clearing both times removes the latest stretch. Changing or removing an existing stretch is
    /// limited to the person who did the work, the current assignee, or an Admin; the change is recorded in the audit log.
    /// </summary>
    Task<Outcome<WorkTaskDetailDto>> SetTimeAsync(Guid id, SetWorkTaskTimeRequest request);

    /// <summary>Starts the live timer: a new stretch of work. Only the current assignee can start or stop it.</summary>
    Task<Outcome<WorkTaskDetailDto>> StartAsync(Guid id);

    Task<Outcome<WorkTaskDetailDto>> StopAsync(Guid id);
}

public sealed class WorkTaskService : IWorkTaskService
{
    /// <summary>One session of a task is a working stretch, not a week: anything longer is a typing mistake (or a timer left running).</summary>
    private static readonly TimeSpan LongestSession = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly INotifier _notifier;
    private readonly IWorkTaskQueries _queries;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public WorkTaskService(
        AppDbContext db, ICurrentUserService currentUser, INotifier notifier,
        IWorkTaskQueries queries, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier;
        _queries = queries;
        _time = time;
    }

    public async Task<Outcome<WorkTaskDetailDto>> CreateAsync(CreateWorkTaskRequest request)
    {
        var assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId
            && u.Role == UserRole.Employee);
        if (assignee is null) return Outcome<WorkTaskDetailDto>.Invalid("Assignee must be a support agent or consultant.");
        if (!assignee.IsActive) return Outcome<WorkTaskDetailDto>.Invalid("That colleague's account is inactive, so a task cannot be assigned to them.");
        if (assignee.DepartmentId is null) return Outcome<WorkTaskDetailDto>.Invalid("Assignee is not linked to a branch.");

        if (_currentUser.Role != UserRole.Admin && assignee.DepartmentId != _currentUser.DepartmentId)
            return Outcome<WorkTaskDetailDto>.Invalid("You can only assign tasks to colleagues in your own branch.");

        var departmentId = assignee.DepartmentId.Value;
        var creator = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);

        var task = new WorkTask
        {
            Title = request.Title,
            Description = request.Description,
            DepartmentId = departmentId,
            CreatedByUserId = creator.Id,
            AssignedByUserId = creator.Id,
            AssignedToUserId = assignee.Id,
            Status = WorkTaskStatus.Pending
        };
        _db.WorkTasks.Add(task);
        _db.WorkTaskAssignmentLogs.Add(new WorkTaskAssignmentLog { WorkTask = task, AssignedByUserId = creator.Id, AssignedToUserId = assignee.Id });
        await _db.SaveChangesAsync();

        await NotifyAssignee(task, assignee, creator);

        return Outcome<WorkTaskDetailDto>.Ok(await _queries.BuildDetailAsync(task.Id));
    }

    public async Task<Outcome<WorkTaskDetailDto>> ReassignAsync(Guid id, ReassignWorkTaskRequest request)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return Outcome<WorkTaskDetailDto>.NotFound();
        if (!WorkTaskAccess.CanAccess(task, _currentUser)) return Outcome<WorkTaskDetailDto>.Forbidden();

        var newAssignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId
            && u.Role == UserRole.Employee);
        if (newAssignee is null) return Outcome<WorkTaskDetailDto>.Invalid("Assignee must be a support agent or consultant.");
        if (!newAssignee.IsActive) return Outcome<WorkTaskDetailDto>.Invalid("That colleague's account is inactive, so a task cannot be assigned to them.");
        if (newAssignee.DepartmentId != task.DepartmentId) return Outcome<WorkTaskDetailDto>.Invalid("You can only reassign tasks to colleagues in the same branch.");

        var reassignedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);

        // If the previous assignee had the timer running, stop it now. Their work stays recorded, but the task is not handed
        // over with a live session that only its old owner could stop (they get a 403, the new owner gets "already running",
        // and the session would otherwise run on for ever, counting to "now" in reports and blocking the old owner's other work).
        var hadRunning = await _db.WorkTaskSessions.AnyAsync(s => s.WorkTaskId == task.Id && s.EndedAtUtc == null);
        await _db.EndRunningTaskSessionsAsync(task.Id, null, UtcNow);
        if (hadRunning) task.Status = WorkTaskStatus.Pending; // handed over: no live work until the new assignee starts

        task.AssignedByUserId = reassignedBy.Id;
        task.AssignedToUserId = newAssignee.Id;
        _db.WorkTaskAssignmentLogs.Add(new WorkTaskAssignmentLog { WorkTaskId = task.Id, AssignedByUserId = reassignedBy.Id, AssignedToUserId = newAssignee.Id });
        await _db.SaveChangesAsync();

        await NotifyAssignee(task, newAssignee, reassignedBy);

        return Outcome<WorkTaskDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<WorkTaskDetailDto>> SetTimeAsync(Guid id, SetWorkTaskTimeRequest request)
    {
        var task = await _db.WorkTasks.Include(t => t.Sessions).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return Outcome<WorkTaskDetailDto>.NotFound();
        if (!WorkTaskAccess.CanAccess(task, _currentUser)) return Outcome<WorkTaskDetailDto>.Forbidden();

        var latest = task.Sessions.OrderByDescending(s => s.StartedAtUtc).FirstOrDefault();
        var remaining = task.Sessions.ToList();

        // A stretch of work belongs to the person who did it. Changing or removing an existing one is allowed only to that
        // person, to whoever the task is assigned to now (who answers for it), or to an Admin — not to any colleague who merely
        // shares the branch. (Entering a brand-new stretch, when the task has none yet, is attributed to the current assignee
        // and open to anyone with access, as it is for tickets.)
        if (latest is not null && !MayChangeSession(latest, task))
            return Outcome<WorkTaskDetailDto>.Forbidden();

        if (request.StartedAtUtc is null && request.EndedAtUtc is null)
        {
            if (latest is not null)
            {
                _db.WorkTaskSessions.Remove(latest);
                remaining.Remove(latest);
                AuditSession("WorkTaskSessionDeleted", task, latest);
            }
        }
        else if (request.StartedAtUtc is null)
        {
            return Outcome<WorkTaskDetailDto>.Invalid("A work session needs a start time.");
        }
        else
        {
            var started = BusinessTimeZone.AsUtc(request.StartedAtUtc.Value);
            DateTime? ended = request.EndedAtUtc is { } end ? BusinessTimeZone.AsUtc(end) : null;

            // Validate the overlap against the owner of THIS stretch (an existing one keeps its owner; a new one is the
            // current assignee's), not blindly against the current assignee — otherwise a previous assignee's stretch was
            // checked against the wrong person's other work.
            var owner = latest?.UserId ?? task.AssignedToUserId;
            var others = task.Sessions.Where(s => s != latest).Select(s => new WorkSessionRules.Span(s.Id, s.StartedAtUtc, s.EndedAtUtc));
            var problem = WorkSessionRules.Validate(started, ended, UtcNow, others, LongestSession, subject: "task")
                ?? WorkSessionRules.ValidateAgainstOtherWork(started, ended, await _db.OtherWorkOfAsync(owner, null, task.Id));
            if (problem is not null) return Outcome<WorkTaskDetailDto>.Invalid(problem);

            if (latest is null)
            {
                latest = new WorkTaskSession { WorkTaskId = task.Id, UserId = owner };
                _db.WorkTaskSessions.Add(latest);
                remaining.Add(latest);
            }
            latest.StartedAtUtc = started;
            latest.EndedAtUtc = ended;
            AuditSession("WorkTaskSessionChanged", task, latest);
        }

        task.Status = remaining.Any(s => s.EndedAtUtc is null) ? WorkTaskStatus.InProgress
            : remaining.Count > 0 ? WorkTaskStatus.Done
            : WorkTaskStatus.Pending;
        await _db.SaveChangesAsync();

        return Outcome<WorkTaskDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<WorkTaskDetailDto>> StartAsync(Guid id)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return Outcome<WorkTaskDetailDto>.NotFound();
        if (task.AssignedToUserId != _currentUser.UserId) return Outcome<WorkTaskDetailDto>.Forbidden();
        if (await _db.WorkTaskSessions.AnyAsync(s => s.WorkTaskId == id && s.EndedAtUtc == null))
            return Outcome<WorkTaskDetailDto>.Invalid("Timer is already running.");

        var now = UtcNow;
        var busy = WorkSessionRules.ValidateAgainstOtherWork(now, null, await _db.OtherWorkOfAsync(task.AssignedToUserId, null, id));
        if (busy is not null) return Outcome<WorkTaskDetailDto>.Invalid(busy);

        _db.WorkTaskSessions.Add(new WorkTaskSession { WorkTaskId = id, UserId = task.AssignedToUserId, StartedAtUtc = now });
        task.Status = WorkTaskStatus.InProgress;
        await _db.SaveChangesAsync();

        return Outcome<WorkTaskDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<WorkTaskDetailDto>> StopAsync(Guid id)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return Outcome<WorkTaskDetailDto>.NotFound();
        if (task.AssignedToUserId != _currentUser.UserId) return Outcome<WorkTaskDetailDto>.Forbidden();

        var running = await _db.WorkTaskSessions.Where(s => s.WorkTaskId == id && s.EndedAtUtc == null).OrderByDescending(s => s.StartedAtUtc).FirstOrDefaultAsync();
        if (running is null) return Outcome<WorkTaskDetailDto>.Invalid("Timer is not currently running.");

        var now = UtcNow;
        running.EndedAtUtc = now > running.StartedAtUtc ? now : running.StartedAtUtc.AddMilliseconds(1);
        task.Status = WorkTaskStatus.Done;
        await _db.SaveChangesAsync();

        return Outcome<WorkTaskDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    /// <summary>Who may change or remove a recorded stretch: the person who did it, the current assignee, or an Admin.</summary>
    private bool MayChangeSession(WorkTaskSession session, WorkTask task) =>
        _currentUser.Role == UserRole.Admin
        || session.UserId == _currentUser.UserId
        || task.AssignedToUserId == _currentUser.UserId;

    private void AuditSession(string action, WorkTask task, WorkTaskSession session) =>
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            Action = action,
            EntityType = nameof(WorkTask),
            EntityId = task.Id,
            Details = $"session {session.Id} of user {session.UserId}: {session.StartedAtUtc:O} - {session.EndedAtUtc?.ToString("O") ?? "running"}"
        });

    private async Task NotifyAssignee(WorkTask task, Infrastructure.Identity.ApplicationUser assignee, Infrastructure.Identity.ApplicationUser assignedBy)
    {
        var assigned = new TaskAssignedEvent(task, assignee, assignedBy);
        _db.Notifications.AddRange(_notifier.InApp(assigned));
        await _db.SaveChangesAsync();

        await _notifier.DeliverAsync(assigned);
    }
}
