using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Filters;
using TicketSystemTech.Api.Reports;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of the reports: it reads the filters, asks the report service and returns the figures (or the Excel file).</summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
[RejectReversedDateRange]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    private static ReportFilter Filter(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId) =>
        new(from, to, companyId, agentId, departmentId, subBranchId);

    [HttpGet("summary")]
    public async Task<ActionResult<ReportSummaryDto>> Summary(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId, CancellationToken ct) =>
        Ok(await _reports.SummaryAsync(Filter(from, to, companyId, agentId, departmentId, subBranchId), ct));

    [HttpGet("timeseries")]
    public async Task<ActionResult<List<TimeSeriesPointDto>>> TimeSeries(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId, CancellationToken ct) =>
        Ok(await _reports.TimeSeriesAsync(Filter(from, to, companyId, agentId, departmentId, subBranchId), ct));

    [HttpGet("leaderboard")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> Leaderboard(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId, CancellationToken ct) =>
        Ok(await _reports.LeaderboardAsync(Filter(from, to, companyId, agentId, departmentId, subBranchId), ct));

    /// <summary>Admin-only: ticket counts broken down by branch, for the global cross-branch dashboard view.
    /// New/open are counted on tickets submitted in the range and closed on tickets closed in it, the same cohorts as the summary.</summary>
    [HttpGet("by-branch")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<List<BranchBreakdownEntryDto>>> ByBranch(DateTime? from, DateTime? to, CancellationToken ct) =>
        Ok(await _reports.ByBranchAsync(from, to, ct));

    /// <summary>Buckets tickets created in range by week/month/quarter/year, so periods can be compared to spot the busiest/quietest ones.</summary>
    [HttpGet("period-comparison")]
    public async Task<ActionResult<List<PeriodComparisonPointDto>>> PeriodComparison(
        DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId, CancellationToken ct, [FromQuery] string groupBy = "month") =>
        Ok(await _reports.PeriodComparisonAsync(Filter(from, to, companyId, agentId, departmentId, subBranchId), groupBy, ct));

    /// <summary>Ranks how many tickets fell under each help topic in the range — the clearest signal of what's causing the most trouble.</summary>
    [HttpGet("top-issues")]
    public async Task<ActionResult<List<TopIssueDto>>> TopIssues(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId, CancellationToken ct, [FromQuery] int limit = 10) =>
        Ok(await _reports.TopIssuesAsync(Filter(from, to, companyId, agentId, departmentId, subBranchId), limit, ct));

    /// <summary>
    /// AI-generated qualitative summary of what went wrong most in a period (min. 1 month), grounded strictly
    /// in the help-topic breakdown for that period — no general knowledge, no invented details.
    /// </summary>
    [HttpPost("ai-insights")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai-staff")]
    public async Task<ActionResult<AiInsightsResponseDto>> AiInsights(AiInsightsRequest request, CancellationToken ct) =>
        this.ToActionResult(await _reports.AiInsightsAsync(request, ct));

    /// <summary>
    /// Downloads the filtered ticket list as an Excel workbook — a "Tickets" sheet with the raw data and a
    /// "Summary" sheet with key stats plus a monthly bar chart. Respects the same branch/company/agent/date
    /// scoping as the other report endpoints.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId, CancellationToken ct)
    {
        var file = await _reports.ExportAsync(Filter(from, to, companyId, agentId, departmentId, subBranchId), ct);
        return File(file.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.FileName);
    }
}
