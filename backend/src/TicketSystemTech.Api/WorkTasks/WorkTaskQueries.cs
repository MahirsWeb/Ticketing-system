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

/// <summary>Reading tasks and the day charts. Admin sees every branch, an employee their own.</summary>
public interface IWorkTaskQueries
{
    Task<List<WorkTaskListItem>> ListAsync(Guid? departmentId, Guid? assignedToUserId, WorkTaskStatus? status, CancellationToken ct);

    Task<Outcome<WorkTaskDetailDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>The task as the current user is shown it. The caller has already checked that they may.</summary>
    Task<WorkTaskDetailDto> BuildDetailAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Combined per-person timeline for one calendar day (in the business time zone, Sla:TimeZoneId: the day the person typed), merging ticket work sessions and task
    /// work sessions: the data source for the Dashboard Gantt chart.
    /// </summary>
    Task<Outcome<GanttResponse>> GanttAsync(Guid userId, DateTime? date, CancellationToken ct);

    /// <summary>Same timeline, but for every Employee in a branch at once.</summary>
    Task<Outcome<BranchGanttResponse>> GanttByBranchAsync(Guid departmentId, DateTime? date, CancellationToken ct);
}

public sealed class WorkTaskQueries : IWorkTaskQueries
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly Lazy<TimeZoneInfo> _zone;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public WorkTaskQueries(AppDbContext db, ICurrentUserService currentUser, IOptions<SlaOptions> slaOptions, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(slaOptions.Value.TimeZoneId));
        _time = time;
    }

    public async Task<List<WorkTaskListItem>> ListAsync(Guid? departmentId, Guid? assignedToUserId, WorkTaskStatus? status, CancellationToken ct)
    {
        var query = _db.WorkTasks.AsNoTracking().AsQueryable();

        query = _currentUser.Role == UserRole.Admin
            ? query
            : query.Where(t => t.DepartmentId == _currentUser.DepartmentId);

        if (departmentId.HasValue) query = query.Where(t => t.DepartmentId == departmentId.Value);
        if (assignedToUserId.HasValue) query = query.Where(t => t.AssignedToUserId == assignedToUserId.Value);
        if (status.HasValue) query = query.Where(t => t.Status == status.Value);

        var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

        var userIds = tasks.SelectMany(t => new[] { t.CreatedByUserId, t.AssignedByUserId, t.AssignedToUserId }).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
        var departmentIds = tasks.Select(t => t.DepartmentId).Distinct().ToList();
        var departments = await _db.Departments.Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var taskIds = tasks.Select(t => t.Id).ToList();
        var summaries = await _db.WorkTaskSessions.AsNoTracking()
            .Where(s => taskIds.Contains(s.WorkTaskId))
            .GroupBy(s => s.WorkTaskId)
            .Select(g => new { TaskId = g.Key, First = g.Min(s => s.StartedAtUtc), Running = g.Any(s => s.EndedAtUtc == null), Last = g.Max(s => s.EndedAtUtc) })
            .ToDictionaryAsync(x => x.TaskId, ct);

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

        return items;
    }

    public async Task<Outcome<WorkTaskDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.WorkTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return Outcome<WorkTaskDetailDto>.NotFound();
        if (!WorkTaskAccess.CanAccess(task, _currentUser)) return Outcome<WorkTaskDetailDto>.Forbidden();

        return Outcome<WorkTaskDetailDto>.Ok(await BuildDetailAsync(id, ct));
    }

    public async Task<Outcome<GanttResponse>> GanttAsync(Guid userId, DateTime? date, CancellationToken ct)
    {
        var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (targetUser is null) return Outcome<GanttResponse>.NotFound();
        if (_currentUser.Role != UserRole.Admin && targetUser.DepartmentId != _currentUser.DepartmentId)
            return Outcome<GanttResponse>.Forbidden();

        var (from, to, localDay) = BusinessTimeZone.DayWindow(date, _zone.Value, UtcNow);
        var entries = await BuildGanttEntriesAsync(userId, from, to, ct);
        return Outcome<GanttResponse>.Ok(new GanttResponse(userId, $"{targetUser.FirstName} {targetUser.LastName}", DateTime.SpecifyKind(localDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), entries));
    }

    public async Task<Outcome<BranchGanttResponse>> GanttByBranchAsync(Guid departmentId, DateTime? date, CancellationToken ct)
    {
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (department is null) return Outcome<BranchGanttResponse>.NotFound();
        if (_currentUser.Role != UserRole.Admin && departmentId != _currentUser.DepartmentId)
            return Outcome<BranchGanttResponse>.Forbidden();

        var (from, to, localDay) = BusinessTimeZone.DayWindow(date, _zone.Value, UtcNow);
        var day = DateTime.SpecifyKind(localDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var employees = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Employee && u.DepartmentId == departmentId && u.IsActive)
            .OrderBy(u => u.FirstName)
            .ToListAsync(ct);

        var users = new List<GanttResponse>();
        foreach (var employee in employees)
        {
            var entries = await BuildGanttEntriesAsync(employee.Id, from, to, ct);
            users.Add(new GanttResponse(employee.Id, $"{employee.FirstName} {employee.LastName}", day, entries));
        }

        return Outcome<BranchGanttResponse>.Ok(new BranchGanttResponse(department.Id, department.Name, day, users));
    }

    /// <summary>
    /// A ticket's blocks on the chart are the work sessions the person logged on it (any number a day). A ticket assigned to
    /// them with no logged work at all still shows, from opening to closing, so it does not disappear just because nobody typed
    /// in a time. People added to a ticket as extra help have no sessions and therefore no block on it.
    /// </summary>
    private async Task<List<GanttEntryDto>> BuildGanttEntriesAsync(Guid userId, DateTime dayStart, DateTime dayEnd, CancellationToken ct)
    {
        var now = UtcNow;
        var blocks = await Queries.TicketWorkBlocks.ForUserAsync(_db, userId, dayStart, dayEnd, null, now, ct);
        var tickets = blocks
            .Select(b => new GanttEntryDto("Ticket", b.TicketId, $"#{b.TicketNumber} — {b.Title}", b.Status.ToString(), b.Start, b.End, b.Status.ToString(), b.IsLogged))
            .ToList();

        // A running stretch of work lasts until now, so on a day that has not happened yet it is not there.
        var tasks = await _db.WorkTaskSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.StartedAtUtc < dayEnd && (s.EndedAtUtc ?? now) >= dayStart)
            .Select(s => new GanttEntryDto("WorkTask", s.WorkTaskId, s.WorkTask!.Title, null, s.StartedAtUtc, s.EndedAtUtc,
                s.EndedAtUtc == null ? nameof(WorkTaskStatus.InProgress) : nameof(WorkTaskStatus.Done), true))
            .ToListAsync(ct);

        return tickets.Concat(tasks).OrderBy(e => e.StartedAtUtc).ToList();
    }

    public async Task<WorkTaskDetailDto> BuildDetailAsync(Guid taskId, CancellationToken ct = default)
    {
        var t = await _db.WorkTasks.AsNoTracking().Include(x => x.Department).FirstAsync(x => x.Id == taskId, ct);
        var history = await _db.WorkTaskAssignmentLogs.AsNoTracking()
            .Where(h => h.WorkTaskId == taskId).OrderBy(h => h.AssignedAtUtc).ToListAsync(ct);

        var userIds = new List<Guid> { t.CreatedByUserId, t.AssignedByUserId, t.AssignedToUserId };
        userIds.AddRange(history.SelectMany(h => new[] { h.AssignedByUserId, h.AssignedToUserId }));
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
        string NameOf(Guid id) => users.GetValueOrDefault(id, "");

        var historyDtos = history.Select(h => new WorkTaskAssignmentLogDto(
            h.AssignedByUserId, NameOf(h.AssignedByUserId), h.AssignedToUserId, NameOf(h.AssignedToUserId), h.AssignedAtUtc
        )).ToList();

        var sessions = await _db.WorkTaskSessions.AsNoTracking().Where(x => x.WorkTaskId == taskId).OrderBy(x => x.StartedAtUtc).ToListAsync(ct);
        var workerIds = sessions.Select(x => x.UserId).Distinct().ToList();
        var workers = await _db.Users.Where(u => workerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
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
