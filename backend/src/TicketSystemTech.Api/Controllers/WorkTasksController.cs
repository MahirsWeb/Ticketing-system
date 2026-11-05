using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Api.WorkTasks;
using TicketSystemTech.Domain.Enums;

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

    private readonly IWorkTaskQueries _queries;
    private readonly IWorkTaskService _tasks;

    public WorkTasksController(IWorkTaskQueries queries, IWorkTaskService tasks)
    {
        _queries = queries;
        _tasks = tasks;
    }

    /// <summary>Staff hand tasks to a colleague in their own branch; Admin may create and assign one into any branch.</summary>
    [HttpPost]
    [Authorize(Roles = $"{StaffOnly},{nameof(UserRole.Admin)}")]
    public async Task<ActionResult<WorkTaskDetailDto>> Create(CreateWorkTaskRequest request) =>
        this.ToActionResult(await _tasks.CreateAsync(request));

    [HttpGet]
    public async Task<ActionResult<List<WorkTaskListItem>>> List(
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] WorkTaskStatus? status,
        CancellationToken ct) =>
        Ok(await _queries.ListAsync(departmentId, assignedToUserId, status, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkTaskDetailDto>> GetById(Guid id, CancellationToken ct) =>
        this.ToActionResult(await _queries.GetAsync(id, ct));

    /// <summary>Hands the task off to someone else in the same branch. Every handoff is recorded in the assignment history.</summary>
    [HttpPost("{id:guid}/reassign")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> Reassign(Guid id, ReassignWorkTaskRequest request) =>
        this.ToActionResult(await _tasks.ReassignAsync(id, request));

    /// <summary>
    /// Manually corrects the time of the task's latest stretch of work (anyone with access to the task, not just the assignee),
    /// or enters one if the task has none yet. Earlier stretches are left alone. Clearing both times removes the latest stretch.
    /// The same rules as for tickets apply: no overlap with another stretch of this task, nothing in the future, and no single
    /// stretch longer than a day.
    /// </summary>
    [HttpPatch("{id:guid}/time")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> SetTime(Guid id, SetWorkTaskTimeRequest request) =>
        this.ToActionResult(await _tasks.SetTimeAsync(id, request));

    /// <summary>
    /// Starts the live timer: a new stretch of work. The earlier ones stay. Only the current assignee — the person actually
    /// doing the work — can start/stop it.
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> Start(Guid id) =>
        this.ToActionResult(await _tasks.StartAsync(id));

    [HttpPost("{id:guid}/stop")]
    [Authorize(Roles = StaffOnly)]
    public async Task<ActionResult<WorkTaskDetailDto>> Stop(Guid id) =>
        this.ToActionResult(await _tasks.StopAsync(id));

    /// <summary>
    /// Combined per-person timeline for one calendar day (in the business time zone, Sla:TimeZoneId: the day the person typed), merging ticket work sessions and task
    /// work sessions — the data source for the Dashboard Gantt chart.
    /// </summary>
    [HttpGet("gantt")]
    public async Task<ActionResult<GanttResponse>> Gantt([FromQuery] Guid userId, [FromQuery] DateTime? date, CancellationToken ct) =>
        this.ToActionResult(await _queries.GanttAsync(userId, date, ct));

    /// <summary>
    /// Same timeline as <see cref="Gantt"/>, but for every Employee in a branch at once — so Admin (or staff,
    /// for their own branch) can see the whole team's day side by side instead of picking one person at a time.
    /// </summary>
    [HttpGet("gantt/branch")]
    public async Task<ActionResult<BranchGanttResponse>> GanttByBranch([FromQuery] Guid departmentId, [FromQuery] DateTime? date, CancellationToken ct) =>
        this.ToActionResult(await _queries.GanttByBranchAsync(departmentId, date, ct));
}
