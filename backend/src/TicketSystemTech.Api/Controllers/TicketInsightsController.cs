using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Filters;
using TicketSystemTech.Api.Reports;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// The reports of the "Tickets" tab. Open to Admin and Employee. They follow the isolation of branches (see <see cref="ITicketInsightService"/>): the branch filter only
/// narrows for Admin.
/// </summary>
[ApiController]
[Route("api/reports/tickets")]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
[RejectReversedDateRange]
public class TicketInsightsController : ControllerBase
{
    private readonly ITicketInsightService _insights;

    public TicketInsightsController(ITicketInsightService insights) => _insights = insights;

    /// <summary>Active tickets by age, without activity for 2, 3 or 7 days, without an assignee, due in the next 24 hours and overdue. Whatever the period.</summary>
    [HttpGet("attention")]
    public async Task<ActionResult<AttentionDto>> Attention(Guid? departmentId, int inactiveDays = 3, CancellationToken ct = default) =>
        this.ToActionResult(await _insights.AttentionAsync(departmentId, inactiveDays, ct));

    /// <summary>Average, median and 90th percentile of the time to solve, for the tickets closed in the period.</summary>
    [HttpGet("resolution")]
    public async Task<ActionResult<ResolutionTimeDto>> Resolution(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct = default) =>
        Ok(await _insights.ResolutionAsync(from, to, departmentId, ct));

    /// <summary>The time to the first reply of staff, for the tickets created in the period.</summary>
    [HttpGet("first-response")]
    public async Task<ActionResult<FirstResponseDto>> FirstResponse(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct = default) =>
        Ok(await _insights.FirstResponseAsync(from, to, departmentId, ct));

    /// <summary>The transfers between branches made in the period.</summary>
    [HttpGet("transfers")]
    public async Task<ActionResult<TransfersDto>> Transfers(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct = default) =>
        Ok(await _insights.TransfersAsync(from, to, departmentId, ct));
}
