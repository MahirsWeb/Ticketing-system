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

    /// <summary>The tickets as they are now, newest first, optionally only those a person is on.</summary>
    [HttpGet("tickets")]
    public async Task<ActionResult<IntegrationTicketPage>> Tickets(
        [FromQuery] string? assigneeEmail, [FromQuery] string? state, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        this.ToActionResult(await _queries.TicketsAsync(assigneeEmail, state, page, pageSize, ct));
}
