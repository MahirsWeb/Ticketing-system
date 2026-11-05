using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Api.Tickets;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// The HTTP face of the tickets: it reads the request, hands it to the service that owns the rule and turns the answer into a
/// status code. What a ticket may do, who may reach it and what is sent to whom lives in <c>TicketSystemTech.Api.Tickets</c>.
/// </summary>
[ApiController]
[Route("api/tickets")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly ITicketQueries _queries;
    private readonly ITicketCreationService _creation;
    private readonly ITicketAssigneeService _assignees;
    private readonly ITicketWorkSessionService _workSessions;
    private readonly ITicketLifecycleService _lifecycle;
    private readonly ITicketMessageService _messages;

    public TicketsController(
        ITicketQueries queries,
        ITicketCreationService creation,
        ITicketAssigneeService assignees,
        ITicketWorkSessionService workSessions,
        ITicketLifecycleService lifecycle,
        ITicketMessageService messages)
    {
        _queries = queries;
        _creation = creation;
        _assignees = assignees;
        _workSessions = workSessions;
        _lifecycle = lifecycle;
        _messages = messages;
    }

    /// <summary>The Idempotency-Key header as it came, or null; whether it is acceptable is for the service to say.</summary>
    private string? RawIdempotencyKey() => Request.Headers.TryGetValue(IdempotencyKey.HeaderName, out var values) ? values.ToString() : null;

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Client))]
    public async Task<ActionResult<TicketDetailDto>> Create(CreateTicketRequest request) =>
        this.ToActionResult(await _creation.CreateAsync(request, RawIdempotencyKey()));

    /// <summary>
    /// Staff creates a ticket on behalf of a client who reported the issue verbally (e.g. by phone) rather
    /// than through the client portal. The ticket is created and opened in one step, and appears exactly as
    /// if the client had submitted it themselves.
    /// </summary>
    [HttpPost("on-behalf")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> CreateOnBehalf(CreateTicketOnBehalfRequest request) =>
        this.ToActionResult(await _creation.CreateOnBehalfAsync(request, RawIdempotencyKey()));

    [HttpGet]
    public async Task<ActionResult<PagedResult<TicketListItem>>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? subBranchId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] bool answeredOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        Ok(await _queries.ListAsync(new TicketListFilter(status, companyId, clientId, assignedToUserId, departmentId, subBranchId,
            search, sortBy, sortDir, createdFrom, createdTo, answeredOnly, page, pageSize), ct));

    /// <summary>
    /// Counts for the "Tickets" nav dropdown: total plus New / Opened (Open+InProgress+Resolved) / Closed,
    /// scoped exactly like <see cref="List"/> so the numbers always match what clicking through shows.
    /// </summary>
    [HttpGet("counts")]
    public async Task<ActionResult<TicketCountsDto>> Counts(CancellationToken ct) => Ok(await _queries.CountsAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> GetById(Guid id, CancellationToken ct) => this.ToActionResult(await _queries.GetAsync(id, ct));

    [HttpPost("{id:guid}/open")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> OpenTicket(Guid id, OpenTicketRequest request) =>
        this.ToActionResult(await _lifecycle.OpenAsync(id, request));

    /// <summary>Moves an already-opened ticket to a different branch. Only staff with access to the ticket's current branch (or Admin) may do this. Notifies everyone in the destination branch.</summary>
    [HttpPost("{id:guid}/transfer-branch")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> TransferBranch(Guid id, TransferTicketBranchRequest request) =>
        this.ToActionResult(await _lifecycle.TransferBranchAsync(id, request));

    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> CloseTicket(Guid id, CloseTicketRequest request) =>
        this.ToActionResult(await _lifecycle.CloseAsync(id, request));

    /// <summary>
    /// Moves a live ticket between Open, InProgress and Resolved. Closing needs a resolution summary and reopening
    /// a closed ticket is its own action, so neither goes through here. Which moves are legal is decided in
    /// <see cref="TicketWorkflow"/>.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> ChangeStatus(Guid id, ChangeTicketStatusRequest request) =>
        this.ToActionResult(await _lifecycle.ChangeStatusAsync(id, request));

    /// <summary>
    /// Reopens a closed ticket — e.g. something was missed and needs to be added or fixed before it's
    /// really done. Any Admin or Employee with access to the ticket can reopen it, not just whoever closed it.
    /// </summary>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> ReopenTicket(Guid id) =>
        this.ToActionResult(await _lifecycle.ReopenAsync(id));

    /// <summary>Adds a co-assignee — e.g. a second person who helped resolve a different part of the same ticket.</summary>
    [HttpPost("{id:guid}/assignees")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> AddAssignee(Guid id, AddTicketAssigneeRequest request) =>
        this.ToActionResult(await _assignees.AddAsync(id, request.UserId));

    [HttpDelete("{id:guid}/assignees/{userId:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> RemoveAssignee(Guid id, Guid userId) =>
        this.ToActionResult(await _assignees.RemoveAsync(id, userId));

    // ---------------- work sessions ----------------

    /// <summary>Logs a stretch of work on the ticket, for the person it is assigned to.</summary>
    [HttpPost("{id:guid}/work-sessions")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> AddWorkSession(Guid id, WorkSessionRequest request) =>
        this.ToActionResult(await _workSessions.AddAsync(id, request));

    /// <summary>Changes the times of a session. It stays with the person who did the work, whoever the ticket belongs to now.</summary>
    [HttpPut("{id:guid}/work-sessions/{sessionId:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> UpdateWorkSession(Guid id, Guid sessionId, WorkSessionRequest request) =>
        this.ToActionResult(await _workSessions.UpdateAsync(id, sessionId, request));

    [HttpDelete("{id:guid}/work-sessions/{sessionId:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<TicketDetailDto>> DeleteWorkSession(Guid id, Guid sessionId) =>
        this.ToActionResult(await _workSessions.DeleteAsync(id, sessionId));

    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<TicketMessageDto>> AddMessage(Guid id, AddTicketMessageRequest request) =>
        this.ToActionResult(await _messages.AddAsync(id, request, RawIdempotencyKey()));
}
