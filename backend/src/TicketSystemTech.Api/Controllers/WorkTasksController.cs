using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

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

    public WorkTasksController(AppDbContext db, ICurrentUserService currentUser, IEmailSender emailSender, INotificationPublisher notificationPublisher)
    {
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

        var items = tasks.Select(t => new WorkTaskListItem(
            t.Id, t.Title, t.Status.ToString(),
            t.DepartmentId, departments.GetValueOrDefault(t.DepartmentId, ""),
            t.CreatedByUserId, users.GetValueOrDefault(t.CreatedByUserId, ""),
            t.AssignedByUserId, users.GetValueOrDefault(t.AssignedByUserId, ""),
            t.AssignedToUserId, users.GetValueOrDefault(t.AssignedToUserId, ""),
            t.StartedAtUtc, t.EndedAtUtc, t.CreatedAt
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

    /// <summary>Manually corrects the start/end time — anyone with access to the task, not just the assignee.</summary>
    [HttpPatch("{id:guid}/time")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> SetTime(Guid id, SetWorkTaskTimeRequest request)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return NotFound();
        if (!CanAccess(task)) return Forbid();
        if (request.StartedAtUtc.HasValue && request.EndedAtUtc.HasValue && request.EndedAtUtc < request.StartedAtUtc)
            return BadRequest(new { message = "End time cannot be before start time." });

        task.StartedAtUtc = request.StartedAtUtc;
        task.EndedAtUtc = request.EndedAtUtc;
        task.Status = task.EndedAtUtc.HasValue ? WorkTaskStatus.Done : task.StartedAtUtc.HasValue ? WorkTaskStatus.InProgress : WorkTaskStatus.Pending;
        await _db.SaveChangesAsync();

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>Starts the live timer. Only the current assignee — the person actually doing the work — can start/stop it.</summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> Start(Guid id)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return NotFound();
        if (task.AssignedToUserId != _currentUser.UserId) return Forbid();
        if (task.StartedAtUtc.HasValue && !task.EndedAtUtc.HasValue) return BadRequest(new { message = "Timer is already running." });

        task.StartedAtUtc = DateTime.UtcNow;
        task.EndedAtUtc = null;
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
        if (!task.StartedAtUtc.HasValue || task.EndedAtUtc.HasValue) return BadRequest(new { message = "Timer is not currently running." });

        task.EndedAtUtc = DateTime.UtcNow;
        task.Status = WorkTaskStatus.Done;
        await _db.SaveChangesAsync();

        return Ok(await BuildDetailDto(id));
    }

    /// <summary>
    /// Combined per-person timeline for one calendar day (UTC), merging ticket work sessions and task
    /// work sessions — the data source for the Dashboard Gantt chart.
    /// </summary>
    [HttpGet("gantt")]
    public async Task<ActionResult<GanttResponse>> Gantt([FromQuery] Guid userId, [FromQuery] DateTime? date)
    {
        var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (targetUser is null) return NotFound();
        if (_currentUser.Role != UserRole.Admin && targetUser.DepartmentId != _currentUser.DepartmentId)
            return Forbid();

        var day = DateTime.SpecifyKind((date ?? DateTime.UtcNow).Date, DateTimeKind.Utc);
        var entries = await BuildGanttEntriesAsync(userId, day);
        return Ok(new GanttResponse(userId, $"{targetUser.FirstName} {targetUser.LastName}", day, entries));
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

        var day = DateTime.SpecifyKind((date ?? DateTime.UtcNow).Date, DateTimeKind.Utc);
        var employees = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Employee && u.DepartmentId == departmentId && u.IsActive)
            .OrderBy(u => u.FirstName)
            .ToListAsync();

        var users = new List<GanttResponse>();
        foreach (var employee in employees)
        {
            var entries = await BuildGanttEntriesAsync(employee.Id, day);
            users.Add(new GanttResponse(employee.Id, $"{employee.FirstName} {employee.LastName}", day, entries));
        }

        return Ok(new BranchGanttResponse(department.Id, department.Name, day, users));
    }

    /// <summary>
    /// A ticket's Gantt block uses the manually-logged work session if staff filled one in (WorkStartedAtUtc/
    /// WorkEndedAtUtc, precise), otherwise falls back to when the ticket was opened/closed — so a ticket shows
    /// up on the timeline as soon as it's opened, without staff having to remember to fill in a separate field.
    /// </summary>
    private async Task<List<GanttEntryDto>> BuildGanttEntriesAsync(Guid userId, DateTime day)
    {
        var dayEnd = day.AddDays(1);

        var ticketRows = await _db.Tickets.AsNoTracking()
            .Where(t => t.AssignedToUserId == userId || t.Assignees.Any(a => a.UserId == userId))
            .Select(t => new
            {
                t.Id,
                t.TicketNumber,
                t.Title,
                t.Status,
                Start = t.WorkStartedAtUtc ?? t.OpenedAtUtc,
                End = t.WorkEndedAtUtc ?? t.ClosedAtUtc
            })
            .Where(t => t.Start != null && t.Start < dayEnd && (t.End == null || t.End >= day))
            .ToListAsync();

        var tickets = ticketRows
            .Select(t => new GanttEntryDto("Ticket", t.Id, $"#{t.TicketNumber} — {t.Title}", t.Status.ToString(), t.Start, t.End, t.Status.ToString()))
            .ToList();

        var tasks = await _db.WorkTasks.AsNoTracking()
            .Where(t => t.AssignedToUserId == userId
                && t.StartedAtUtc != null
                && t.StartedAtUtc < dayEnd
                && (t.EndedAtUtc == null || t.EndedAtUtc >= day))
            .Select(t => new GanttEntryDto("WorkTask", t.Id, t.Title, null, t.StartedAtUtc, t.EndedAtUtc, t.Status.ToString()))
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

        return new WorkTaskDetailDto(
            t.Id, t.Title, t.Description, t.Status.ToString(),
            t.DepartmentId, t.Department?.Name ?? "",
            t.CreatedByUserId, NameOf(t.CreatedByUserId),
            t.AssignedByUserId, NameOf(t.AssignedByUserId),
            t.AssignedToUserId, NameOf(t.AssignedToUserId),
            t.StartedAtUtc, t.EndedAtUtc, t.CreatedAt, historyDtos
        );
    }
}
