using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using Microsoft.Extensions.Options;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Application.WorkTime;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// Informal work items ("tasks") that staff hand to each other alongside ticket work — e.g. "check the
/// Bingo contract data". Employees create, assign and reassign these among themselves within their own
/// branch. Admin can also create/assign one into any branch, but otherwise only has read-only visibility
/// (no reassigning or timer control on tasks already in progress); clients have no access at all.
/// </summary>
[ApiController]
[Route("api/work-tasks")]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
public class WorkTasksController : ControllerBase
{
    private const string StaffOnly = nameof(UserRole.Employee);

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailSender _emailSender;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly Lazy<TimeZoneInfo> _zone;

    /// <summary>One session of a task is a working stretch, not a week: anything longer is a typing mistake (or a timer left running).</summary>
    private static readonly TimeSpan LongestSession = TimeSpan.FromHours(24);

    public WorkTasksController(AppDbContext db, ICurrentUserService currentUser, IEmailSender emailSender, INotificationPublisher notificationPublisher,
        IOptions<SlaOptions> slaOptions)
    {
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(slaOptions.Value.TimeZoneId));
        _db = db;
        _currentUser = currentUser;
        _emailSender = emailSender;
        _notificationPublisher = notificationPublisher;
    }

    /// <summary>Staff hand tasks to a colleague in their own branch; Admin may create and assign one into any branch.</summary>
    [HttpPost]
    [Authorize(Roles = $"{StaffOnly},{nameof(UserRole.Admin)}")]
    public async Task<ActionResult<WorkTaskDetailDto>> Create(CreateWorkTaskRequest request)
    {
        var assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId
            && u.Role == UserRole.Employee);
        if (assignee is null) return BadRequest(new { message = "Assignee must be a support agent or consultant." });
        if (assignee.DepartmentId is null) return BadRequest(new { message = "Assignee is not linked to a branch." });

        if (_currentUser.Role != UserRole.Admin && assignee.DepartmentId != _currentUser.DepartmentId)
            return BadRequest(new { message = "You can only assign tasks to colleagues in your own branch." });

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

        return Ok(await BuildDetailDto(task.Id));
    }

    [HttpGet]
    public async Task<ActionResult<List<WorkTaskListItem>>> List(
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] WorkTaskStatus? status)
    {
        var query = _db.WorkTasks.AsNoTracking().AsQueryable();

        query = _currentUser.Role == UserRole.Admin
            ? query
            : query.Where(t => t.DepartmentId == _currentUser.DepartmentId);

        if (departmentId.HasValue) query = query.Where(t => t.DepartmentId == departmentId.Value);
        if (assignedToUserId.HasValue) query = query.Where(t => t.AssignedToUserId == assignedToUserId.Value);
        if (status.HasValue) query = query.Where(t => t.Status == status.Value);

        var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

        var userIds = tasks.SelectMany(t => new[] { t.CreatedByUserId, t.AssignedByUserId, t.AssignedToUserId }).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName);
        var departmentIds = tasks.Select(t => t.DepartmentId).Distinct().ToList();
        var departments = await _db.Departments.Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name);

        var taskIds = tasks.Select(t => t.Id).ToList();
        var summaries = await _db.WorkTaskSessions.AsNoTracking()
            .Where(s => taskIds.Contains(s.WorkTaskId))
            .GroupBy(s => s.WorkTaskId)
            .Select(g => new { TaskId = g.Key, First = g.Min(s => s.StartedAtUtc), Running = g.Any(s => s.EndedAtUtc == null), Last = g.Max(s => s.EndedAtUtc) })
            .ToDictionaryAsync(x => x.TaskId);

        var items = tasks.Select(t => new WorkTaskListItem(
            t.Id, t.Title, t.Status.ToString(),
            t.DepartmentId, departments.GetValueOrDefault(t.DepartmentId, ""),
            t.CreatedByUserId, users.GetValueOrDefault(t.CreatedByUserId, ""),
            t.AssignedByUserId, users.GetValueOrDefault(t.AssignedByUserId, ""),
            t.AssignedToUserId, users.GetValueOrDefault(t.AssignedToUserId, ""),
            summaries.TryGetValue(t.Id, out var sum) ? sum.First : null,
            sum is null || sum.Running ? null : sum.Last,
            t.CreatedAt
        )).ToList();

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkTaskDetailDto>> GetById(Guid id)
    {
        var task = await _db.WorkTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return NotFound();
        if (!CanAccess(task)) return Forbid();

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>Hands the task off to someone else in the same branch. Every handoff is recorded in the assignment history.</summary>
    [HttpPost("{id:guid}/reassign")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> Reassign(Guid id, ReassignWorkTaskRequest request)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return NotFound();
        if (!CanAccess(task)) return Forbid();

        var newAssignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId
            && u.Role == UserRole.Employee);
        if (newAssignee is null) return BadRequest(new { message = "Assignee must be a support agent or consultant." });
        if (newAssignee.DepartmentId != task.DepartmentId) return BadRequest(new { message = "You can only reassign tasks to colleagues in the same branch." });

        var reassignedBy = await _db.Users.FirstAsync(u => u.Id == _currentUser.UserId);

        task.AssignedByUserId = reassignedBy.Id;
        task.AssignedToUserId = newAssignee.Id;
        _db.WorkTaskAssignmentLogs.Add(new WorkTaskAssignmentLog { WorkTaskId = task.Id, AssignedByUserId = reassignedBy.Id, AssignedToUserId = newAssignee.Id });
        await _db.SaveChangesAsync();

        await NotifyAssignee(task, newAssignee, reassignedBy);

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>
    /// Manually corrects the time of the task's latest stretch of work (anyone with access to the task, not just the assignee),
    /// or enters one if the task has none yet. Earlier stretches are left alone. Clearing both times removes the latest stretch.
    /// The same rules as for tickets apply: no overlap with another stretch of this task, nothing in the future, and no single
    /// stretch longer than a day.
    /// </summary>
    [HttpPatch("{id:guid}/time")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> SetTime(Guid id, SetWorkTaskTimeRequest request)
    {
        var task = await _db.WorkTasks.Include(t => t.Sessions).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return NotFound();
        if (!CanAccess(task)) return Forbid();

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
            return BadRequest(new { message = "A work session needs a start time." });
        }
        else
        {
            var started = BusinessTimeZone.AsUtc(request.StartedAtUtc.Value);
            DateTime? ended = request.EndedAtUtc is { } end ? BusinessTimeZone.AsUtc(end) : null;

            var others = task.Sessions.Where(s => s != latest).Select(s => new WorkSessionRules.Span(s.Id, s.StartedAtUtc, s.EndedAtUtc));
            var problem = WorkSessionRules.Validate(started, ended, DateTime.UtcNow, others, LongestSession, subject: "task")
                ?? WorkSessionRules.ValidateAgainstOtherWork(started, ended, await _db.OtherWorkOfAsync(task.AssignedToUserId, null, task.Id));
            if (problem is not null) return BadRequest(new { message = problem });

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

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>
    /// Starts the live timer: a new stretch of work. The earlier ones stay. Only the current assignee — the person actually
    /// doing the work — can start/stop it.
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> Start(Guid id)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return NotFound();
        if (task.AssignedToUserId != _currentUser.UserId) return Forbid();
        if (await _db.WorkTaskSessions.AnyAsync(s => s.WorkTaskId == id && s.EndedAtUtc == null))
            return BadRequest(new { message = "Timer is already running." });

        var now = DateTime.UtcNow;
        var busy = WorkSessionRules.ValidateAgainstOtherWork(now, null, await _db.OtherWorkOfAsync(task.AssignedToUserId, null, id));
        if (busy is not null) return BadRequest(new { message = busy });

        _db.WorkTaskSessions.Add(new WorkTaskSession { WorkTaskId = id, UserId = task.AssignedToUserId, StartedAtUtc = now });
        task.Status = WorkTaskStatus.InProgress;
        await _db.SaveChangesAsync();

        return Ok(await BuildDetailDto(id));
    }

    [HttpPost("{id:guid}/stop")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> Stop(Guid id)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return NotFound();
        if (task.AssignedToUserId != _currentUser.UserId) return Forbid();

        var running = await _db.WorkTaskSessions.Where(s => s.WorkTaskId == id && s.EndedAtUtc == null).OrderByDescending(s => s.StartedAtUtc).FirstOrDefaultAsync();
        if (running is null) return BadRequest(new { message = "Timer is not currently running." });

        var now = DateTime.UtcNow;
        running.EndedAtUtc = now > running.StartedAtUtc ? now : running.StartedAtUtc.AddMilliseconds(1);
        task.Status = WorkTaskStatus.Done;
        await _db.SaveChangesAsync();

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>
    /// Combined per-person timeline for one calendar day (in the business time zone, Sla:TimeZoneId: the day the person typed), merging ticket work sessions and task
    /// work sessions — the data source for the Dashboard Gantt chart.
    /// </summary>
    [HttpGet("gantt")]
    public async Task<ActionResult<GanttResponse>> Gantt([FromQuery] Guid userId, [FromQuery] DateTime? date)
    {
        var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (targetUser is null) return NotFound();
        if (_currentUser.Role != UserRole.Admin && targetUser.DepartmentId != _currentUser.DepartmentId)
            return Forbid();

        var (from, to, localDay) = BusinessTimeZone.DayWindow(date, _zone.Value, DateTime.UtcNow);
        var entries = await BuildGanttEntriesAsync(userId, from, to);
        return Ok(new GanttResponse(userId, $"{targetUser.FirstName} {targetUser.LastName}", DateTime.SpecifyKind(localDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), entries));
    }

    /// <summary>
    /// Same timeline as <see cref="Gantt"/>, but for every Employee in a branch at once — so Admin (or staff,
    /// for their own branch) can see the whole team's day side by side instead of picking one person at a time.
    /// </summary>
    [HttpGet("gantt/branch")]
    public async Task<ActionResult<BranchGanttResponse>> GanttByBranch([FromQuery] Guid departmentId, [FromQuery] DateTime? date)
    {
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId);
        if (department is null) return NotFound();
        if (_currentUser.Role != UserRole.Admin && departmentId != _currentUser.DepartmentId)
            return Forbid();

        var (from, to, localDay) = BusinessTimeZone.DayWindow(date, _zone.Value, DateTime.UtcNow);
        var day = DateTime.SpecifyKind(localDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var employees = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Employee && u.DepartmentId == departmentId && u.IsActive)
            .OrderBy(u => u.FirstName)
            .ToListAsync();

        var users = new List<GanttResponse>();
        foreach (var employee in employees)
        {
            var entries = await BuildGanttEntriesAsync(employee.Id, from, to);
            users.Add(new GanttResponse(employee.Id, $"{employee.FirstName} {employee.LastName}", day, entries));
        }

        return Ok(new BranchGanttResponse(department.Id, department.Name, day, users));
    }

    /// <summary>
    /// A ticket's blocks on the chart are the work sessions the person logged on it (any number a day). A ticket assigned to
    /// them with no logged work at all still shows, from opening to closing, so it does not disappear just because nobody typed
    /// in a time. People added to a ticket as extra help have no sessions and therefore no block on it.
    /// </summary>
    private async Task<List<GanttEntryDto>> BuildGanttEntriesAsync(Guid userId, DateTime dayStart, DateTime dayEnd)
    {
        var now = DateTime.UtcNow;
        var blocks = await Queries.TicketWorkBlocks.ForUserAsync(_db, userId, dayStart, dayEnd, null, now);
        var tickets = blocks
            .Select(b => new GanttEntryDto("Ticket", b.TicketId, $"#{b.TicketNumber} — {b.Title}", b.Status.ToString(), b.Start, b.End, b.Status.ToString(), b.IsLogged))
            .ToList();

        // A running stretch of work lasts until now, so on a day that has not happened yet it is not there.
        var tasks = await _db.WorkTaskSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.StartedAtUtc < dayEnd && (s.EndedAtUtc ?? now) >= dayStart)
            .Select(s => new GanttEntryDto("WorkTask", s.WorkTaskId, s.WorkTask!.Title, null, s.StartedAtUtc, s.EndedAtUtc,
                s.EndedAtUtc == null ? nameof(WorkTaskStatus.InProgress) : nameof(WorkTaskStatus.Done), true))
            .ToListAsync();

        return tickets.Concat(tasks).OrderBy(e => e.StartedAtUtc).ToList();
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

    private bool CanAccess(WorkTask task) =>
        _currentUser.Role == UserRole.Admin || task.DepartmentId == _currentUser.DepartmentId;

    private async Task<WorkTaskDetailDto> BuildDetailDto(Guid taskId)
    {
        var t = await _db.WorkTasks.AsNoTracking().Include(x => x.Department).FirstAsync(x => x.Id == taskId);
        var history = await _db.WorkTaskAssignmentLogs.AsNoTracking()
            .Where(h => h.WorkTaskId == taskId).OrderBy(h => h.AssignedAtUtc).ToListAsync();

        var userIds = new List<Guid> { t.CreatedByUserId, t.AssignedByUserId, t.AssignedToUserId };
        userIds.AddRange(history.SelectMany(h => new[] { h.AssignedByUserId, h.AssignedToUserId }));
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName);
        string NameOf(Guid id) => users.GetValueOrDefault(id, "");

        var historyDtos = history.Select(h => new WorkTaskAssignmentLogDto(
            h.AssignedByUserId, NameOf(h.AssignedByUserId), h.AssignedToUserId, NameOf(h.AssignedToUserId), h.AssignedAtUtc
        )).ToList();

        var sessions = await _db.WorkTaskSessions.AsNoTracking().Where(x => x.WorkTaskId == taskId).OrderBy(x => x.StartedAtUtc).ToListAsync();
        var workerIds = sessions.Select(x => x.UserId).Distinct().ToList();
        var workers = await _db.Users.Where(u => workerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName);
        var sessionDtos = sessions.Select(x => new WorkTaskSessionDto(x.Id, x.UserId, workers.GetValueOrDefault(x.UserId, ""), x.StartedAtUtc, x.EndedAtUtc)).ToList();
        DateTime? firstStart = sessions.Count == 0 ? null : sessions[0].StartedAtUtc;
        DateTime? lastEnd = sessions.Count == 0 || sessions.Any(x => x.EndedAtUtc is null) ? null : sessions.Max(x => x.EndedAtUtc);

        return new WorkTaskDetailDto(
            t.Id, t.Title, t.Description, t.Status.ToString(),
            t.DepartmentId, t.Department?.Name ?? "",
            t.CreatedByUserId, NameOf(t.CreatedByUserId),
            t.AssignedByUserId, NameOf(t.AssignedByUserId),
            t.AssignedToUserId, NameOf(t.AssignedToUserId),
            firstStart, lastEnd, t.CreatedAt, historyDtos, sessionDtos
        );
    }
}
