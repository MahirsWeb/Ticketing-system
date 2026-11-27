using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Filters;
using TicketSystemTech.Api.Reports;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// The statistics of the companies, for the second tab of the overview. Open to Admin and Employee, and the same for both: all branches together, unlike the
/// reports of <see cref="ReportsController"/>, which are limited to the branch of the person asking. Nothing here returns a ticket; see <see cref="ICompanyReportService"/>.
/// Pages and lists have an upper limit, whatever is asked.
/// </summary>
[ApiController]
[Route("api/reports/companies")]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
[RejectReversedDateRange]
public class CompanyReportsController : ControllerBase
{
    private readonly ICompanyReportService _reports;

    public CompanyReportsController(ICompanyReportService reports) => _reports = reports;

    private static CompanyReportFilter Filter(DateTime? from, DateTime? to, Guid? companyId = null, Guid? clientId = null) => new(from, to, companyId, clientId);

    /// <summary>The companies with the most tickets created in the period (ten at most), with what part of all tickets they make.</summary>
    [HttpGet("top")]
    public async Task<ActionResult<TopCompaniesDto>> Top(DateTime? from, DateTime? to, int limit = CompanyReportService.MostTop, CancellationToken ct = default) =>
        Ok(await _reports.TopCompaniesAsync(Filter(from, to), limit, ct));

    /// <summary>Every company, a page at a time (most a hundred), searched by name and sorted by total, name, previous, growth, avgresolution, sla, open or share.</summary>
    [HttpGet("list")]
    public async Task<ActionResult<CompanyListPageDto>> List(DateTime? from, DateTime? to, string? search, string? sort, bool descending = true, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        Ok(await _reports.ListCompaniesAsync(Filter(from, to), search, sort, descending, page, pageSize, ct));

    [HttpGet("summary")]
    public async Task<ActionResult<CompanySummaryDto>> Summary(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.SummaryAsync(Filter(from, to, companyId, clientId), ct));

    /// <summary>The people of one company who reported the most tickets.</summary>
    [HttpGet("clients")]
    public async Task<ActionResult<List<CompanyClientStatDto>>> Clients(DateTime? from, DateTime? to, Guid? companyId, int limit = 10, CancellationToken ct = default) =>
        this.ToActionResult(await _reports.ClientsAsync(Filter(from, to, companyId), limit, ct));

    [HttpGet("topics")]
    public async Task<ActionResult<List<BreakdownItemDto>>> Topics(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.TopicsAsync(Filter(from, to, companyId, clientId), ct));

    [HttpGet("categories")]
    public async Task<ActionResult<List<BreakdownItemDto>>> Categories(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.CategoriesAsync(Filter(from, to, companyId, clientId), ct));

    [HttpGet("branches")]
    public async Task<ActionResult<List<BranchCountDto>>> Branches(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.BranchesAsync(Filter(from, to, companyId, clientId), ct));

    /// <summary>Who closed the tickets that were closed in the period, and in which branches those tickets were.</summary>
    [HttpGet("closed-by")]
    public async Task<ActionResult<ClosedByDto>> ClosedBy(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, int limit = 10, CancellationToken ct = default) =>
        this.ToActionResult(await _reports.ClosedByAsync(Filter(from, to, companyId, clientId), limit, ct));

    [HttpGet("timeseries")]
    public async Task<ActionResult<List<TimeSeriesPointDto>>> TimeSeries(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.TimeSeriesAsync(Filter(from, to, companyId, clientId), ct));

    /// <summary>The tickets not closed now, by age: up to 2 days, up to 7, up to 30, older, and how old the oldest is. Whatever the period.</summary>
    [HttpGet("open-age")]
    public async Task<ActionResult<OpenAgeDto>> OpenAge(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.OpenAgeAsync(Filter(from, to, companyId, clientId), ct));

    /// <summary>The time written down on tickets: work sessions that started in the period.</summary>
    [HttpGet("time-spent")]
    public async Task<ActionResult<TimeSpentDto>> TimeSpent(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.TimeSpentAsync(Filter(from, to, companyId, clientId), ct));

    /// <summary>Replies per ticket, and the time from the report to the pick-up, for the tickets created in the period.</summary>
    [HttpGet("conversation")]
    public async Task<ActionResult<ConversationDto>> Conversation(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.ConversationAsync(Filter(from, to, companyId, clientId), ct));

    /// <summary>When the tickets of the period were created: by weekday and by hour, in the business time zone.</summary>
    [HttpGet("rhythm")]
    public async Task<ActionResult<RhythmDto>> Rhythm(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct) =>
        this.ToActionResult(await _reports.RhythmAsync(Filter(from, to, companyId, clientId), ct));

    /// <summary>The same person reporting the same topic more than once in the period.</summary>
    [HttpGet("repeated-topics")]
    public async Task<ActionResult<RepeatedTopicsDto>> RepeatedTopics(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, int limit = 10, CancellationToken ct = default) =>
        this.ToActionResult(await _reports.RepeatedTopicsAsync(Filter(from, to, companyId, clientId), limit, ct));

    /// <summary>The companies that had tickets in the period right before this one and have none in this one.</summary>
    [HttpGet("quiet")]
    public async Task<ActionResult<QuietCompaniesDto>> Quiet(DateTime? from, DateTime? to, int limit = 10, CancellationToken ct = default) =>
        Ok(await _reports.QuietCompaniesAsync(Filter(from, to), limit, ct));

    /// <summary>The company as a whole: first and last ticket, tickets by year, registered and active clients, hours. Needs a company.</summary>
    [HttpGet("card")]
    public async Task<ActionResult<CompanyCardDto>> Card(DateTime? from, DateTime? to, Guid? companyId, CancellationToken ct) =>
        this.ToActionResult(await _reports.CardAsync(Filter(from, to, companyId), ct));

    /// <summary>The branches that work for the company: tickets, closed, hours of their staff, share. Needs a company.</summary>
    [HttpGet("branch-load")]
    public async Task<ActionResult<BranchLoadDto>> BranchLoad(DateTime? from, DateTime? to, Guid? companyId, CancellationToken ct) =>
        this.ToActionResult(await _reports.BranchLoadAsync(Filter(from, to, companyId), ct));

    /// <summary>The workbook of the same figures: what is on the screen for the same choice.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(DateTime? from, DateTime? to, Guid? companyId, Guid? clientId, CancellationToken ct)
    {
        var outcome = await _reports.ExportAsync(Filter(from, to, companyId, clientId), ct);
        if (!outcome.Succeeded) return this.ToResult(outcome);
        return File(outcome.Value!.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", outcome.Value.FileName);
    }
}
