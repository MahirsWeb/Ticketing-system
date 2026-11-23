using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Integration;
using TicketSystemTech.Api.Security;
using TicketSystemTech.Api.Services;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// The HTTP face of the reading of tickets by another application (see docs/integration-api.md). It is signed into by an API key in the header
/// X-Api-Key and by nothing else: a person's token is refused here, and the key opens nothing outside this controller. Read only. It is not in
/// the Swagger description of the application (the description lives in docs/integration-api.md) so that the generated types of the web
/// page do not grow a part nobody there uses.
/// </summary>
[ApiController]
[Route("api/integration")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[EnableRateLimiting(RateLimitPolicies.Integration)]
[ApiExplorerSettings(IgnoreApi = true)]
public class IntegrationController : ControllerBase
{
    private readonly IIntegrationQueries _queries;

    public IntegrationController(IIntegrationQueries queries) => _queries = queries;

    /// <summary>Who was put on which ticket since a time, oldest first, at most <paramref name="limit"/> (500) at a time.</summary>
    [HttpGet("assignments")]
    public async Task<ActionResult<IntegrationAssignmentPage>> Assignments(
        [FromQuery] string? since, [FromQuery] Guid? afterId, [FromQuery] string? email, CancellationToken ct, [FromQuery] int limit = 100) =>
        this.ToActionResult(await _queries.AssignmentsAsync(since, afterId, email, limit, ct));

    /// <summary>
    /// What happened to tickets since a time (opened, somebody put on or taken off, status changed, closed, reopened, moved to another branch),
    /// oldest first, at most <paramref name="limit"/> (500) at a time. It says THAT something happened and to whom; the ticket in the answer is as it is now.
    /// </summary>
    [HttpGet("events")]
    public async Task<ActionResult<IntegrationEventPage>> Events(
        [FromQuery] string? since, [FromQuery] Guid? afterId, CancellationToken ct, [FromQuery] int limit = 100) =>
        this.ToActionResult(await _queries.EventsAsync(since, afterId, limit, ct));

    /// <summary>
    /// The tickets as they are now, newest first (by the time they were created), optionally only those a person is on and only those created in a
    /// period (<c>createdFrom</c> included, <c>createdTo</c> not). <c>includeWork=true</c> adds who worked how long on each.
    /// </summary>
    [HttpGet("tickets")]
    public async Task<ActionResult<IntegrationTicketPage>> Tickets(
        [FromQuery] string? assigneeEmail, [FromQuery] string? state, [FromQuery] string? createdFrom, [FromQuery] string? createdTo, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] bool includeWork = false) =>
        this.ToActionResult(await _queries.TicketsAsync(assigneeEmail, state, createdFrom, createdTo, page, pageSize, includeWork, ct));

    /// <summary>
    /// Who worked, when and for how long: one entry per stretch of work of one person, on a ticket or on a task, by the day it started on. The period is
    /// given as days (<c>from</c>, <c>to</c>, yyyy-MM-dd), at most 92. What is no longer in the period read again was deleted, or moved to another day.
    /// </summary>
    [HttpGet("time-entries")]
    public async Task<ActionResult<IntegrationTimeEntryPage>> TimeEntries(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? email, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100) =>
        this.ToActionResult(await _queries.TimeEntriesAsync(from, to, email, page, pageSize, ct));

    /// <summary>One ticket as it is now, whatever its status, or 404. To see why a ticket is no longer in somebody's list.</summary>
    [HttpGet("tickets/{id:guid}")]
    public async Task<ActionResult<IntegrationTicket>> Ticket(Guid id, CancellationToken ct, [FromQuery] bool includeWork = false) =>
        this.ToActionResult(await _queries.TicketAsync(id, includeWork, ct));
}
