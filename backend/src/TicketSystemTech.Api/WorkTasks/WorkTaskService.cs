using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
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
    /// Manually corrects the time of the task's latest stretch of work (anyone with access to the task, not just the assignee),
    /// or enters one if the task has none yet. Earlier stretches are left alone. Clearing both times removes the latest stretch.
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
    private readonly IEmailSender _emailSender;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IWorkTaskQueries _queries;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public WorkTaskService(
        AppDbContext db, ICurrentUserService currentUser, IEmailSender emailSender, INotificationPublisher notificationPublisher,
        IWorkTaskQueries queries, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _emailSender = emailSender;
        _notificationPublisher = notificationPublisher;
        _queries = queries;
        _time = time;
    }

    public async Task<Outcome<WorkTaskDetailDto>> CreateAsync(CreateWorkTaskRequest request)
    {
        var assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId
            && u.Role == UserRole.Employee);
        if (assignee is null) return Outcome<WorkTaskDetailDto>.Invalid("Assignee must be a support agent or consultant.");
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
        if (newAssignee.DepartmentId != task.DepartmentId) return Outcome<WorkTaskDetailDto>.Invalid("You can only reassign tasks to colleagues in the same branch.");

        var reassignedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);

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

        if (request.StartedAtUtc is null && request.EndedAtUtc is null)
        {
            if (latest is not null)
            {
                _db.WorkTaskSessions.Remove(latest);
                remaining.Remove(latest);
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

            var others = task.Sessions.Where(s => s != latest).Select(s => new WorkSessionRules.Span(s.Id, s.StartedAtUtc, s.EndedAtUtc));
            var problem = WorkSessionRules.Validate(started, ended, UtcNow, others, LongestSession, subject: "task")
                ?? WorkSessionRules.ValidateAgainstOtherWork(started, ended, await _db.OtherWorkOfAsync(task.AssignedToUserId, null, task.Id));
            if (problem is not null) return Outcome<WorkTaskDetailDto>.Invalid(problem);

            if (latest is null)
            {
                latest = new WorkTaskSession { WorkTaskId = task.Id, UserId = task.AssignedToUserId };
                _db.WorkTaskSessions.Add(latest);
                remaining.Add(latest);
            }
            latest.StartedAtUtc = started;
            latest.EndedAtUtc = ended;
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

    private async Task NotifyAssignee(WorkTask task, Infrastructure.Identity.ApplicationUser assignee, Infrastructure.Identity.ApplicationUser assignedBy)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = assignee.Id,
            Type = NotificationType.TaskAssigned,
            Message = $"{assignedBy.FirstName} {assignedBy.LastName} assigned you a task: \"{task.Title}\""
        });
        await _db.SaveChangesAsync();

        await _emailSender.SendAsync(assignee.Email!, "A task has been assigned to you",
            EmailTemplates.WorkTaskAssigned(assignee.FirstName, task.Title, $"{assignedBy.FirstName} {assignedBy.LastName}"));
        await _notificationPublisher.PushToUserAsync(assignee.Id, "taskAssigned", new { task.Id, task.Title });
    }
}
