using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using Microsoft.Extensions.Options;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Application.Reporting;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
public class ReportsController : ControllerBase
{
    private const string InsightsSystemInstruction =
        "You are analyzing helpdesk ticket statistics for management. Based ONLY on the category breakdown " +
        "given, write a short summary (max 6 sentences) identifying which problem categories caused the most " +
        "trouble, any patterns worth noting, and 1-2 concrete recommendations to reduce them. Do not invent " +
        "details that aren't present in the data. Reply in the same language as the question.";

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly Lazy<TimeZoneInfo> _zone;

    public ReportsController(AppDbContext db, ICurrentUserService currentUser, IChatCompletionService chatCompletionService, IOptions<SlaOptions> slaOptions)
    {
        _db = db;
        _currentUser = currentUser;
        _chatCompletionService = chatCompletionService;
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(slaOptions.Value.TimeZoneId));
    }

    /// <summary>The branch/company/agent scope shared by every report, with no date restriction.</summary>
    /// <summary>
    /// Fires when the browser gives up on the request (the user navigates away, presses "Stop", or the connection
    /// drops). It is handed to the database and to the AI calls below so the server stops working for nobody:
    /// without it an abandoned AI question or Excel export keeps running to the end.
    /// </summary>
    private CancellationToken Ct => HttpContext.RequestAborted;

    private IQueryable<Ticket> InScope(Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();

        // "Tickets of agent X" means the ones they are (co-)assigned to, not only those where they are the primary
        // assignee — the ticket list already works this way, so the two screens now agree.
        if (agentId.HasValue)
            query = query.Where(t => t.AssignedToUserId == agentId.Value || t.Assignees.Any(a => a.UserId == agentId.Value));

        if (companyId.HasValue) query = query.Where(t => t.CompanyId == companyId.Value);
        if (subBranchId.HasValue) query = query.Where(t => t.SubBranchId == subBranchId.Value);

        // Branches are isolated: non-admin staff only ever see their own branch's data, regardless of what's requested.
        if (_currentUser.Role != UserRole.Admin)
            query = query.Where(t => t.DepartmentId == _currentUser.DepartmentId);
        else if (departmentId.HasValue)
            query = query.Where(t => t.DepartmentId == departmentId.Value);

        return query;
    }

    /// <summary>Tickets submitted in the range — the cohort for "what came in" figures.</summary>
    private IQueryable<Ticket> Scoped(DateTime from, DateTime to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId = null) =>
        InScope(companyId, agentId, departmentId, subBranchId).Where(t => t.CreatedAt >= from && t.CreatedAt <= to);

    /// <summary>Tickets closed in the range — the cohort for "what got done" figures (closed count, resolution time,
    /// SLA compliance). Everything used to be measured on tickets CREATED in the range, so a ticket opened last month
    /// and closed today counted toward last month, and the labels on the dashboard did not mean what they said.</summary>
    private IQueryable<Ticket> ClosedIn(DateTime from, DateTime to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId) =>
        InScope(companyId, agentId, departmentId, subBranchId)
            .Where(t => t.ClosedAtUtc != null && t.ClosedAtUtc >= from && t.ClosedAtUtc <= to);

    [HttpGet("summary")]
    public async Task<ActionResult<ReportSummaryDto>> Summary(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId)
    {
        var (f, t) = NormalizeRange(from, to);

        var createdStatuses = await Scoped(f, t, companyId, agentId, departmentId, subBranchId).Select(x => x.Status).ToListAsync(Ct);
        var totalNew = createdStatuses.Count(x => x == TicketStatus.New);
        // Resolved is not closed yet, so it is still work in progress.
        var totalOpen = createdStatuses.Count(x => x is TicketStatus.Open or TicketStatus.InProgress or TicketStatus.Resolved);

        var closed = await ClosedIn(f, t, companyId, agentId, departmentId, subBranchId)
            .Select(x => new { x.OpenedAtUtc, x.ClosedAtUtc, x.DueDateUtc })
            .ToListAsync(Ct);

        var resolutionHours = closed
            .Where(x => x.OpenedAtUtc.HasValue && x.ClosedAtUtc.HasValue)
            .Select(x => (x.ClosedAtUtc!.Value - x.OpenedAtUtc!.Value).TotalHours)
            .ToList();
        var avgResolution = resolutionHours.Count > 0 ? resolutionHours.Average() : 0;

        var closedWithDueDate = closed.Where(x => x.DueDateUtc.HasValue).ToList();
        var compliant = closedWithDueDate.Count(x => x.ClosedAtUtc <= x.DueDateUtc);
        // No closed tickets with a due date means there is nothing to measure, which must not read as perfect compliance.
        double? slaRate = closedWithDueDate.Count > 0 ? Math.Round((double)compliant / closedWithDueDate.Count * 100, 1) : null;

        return Ok(new ReportSummaryDto(totalNew, totalOpen, closed.Count, Math.Round(avgResolution, 1), slaRate));
    }

    [HttpGet("timeseries")]
    public async Task<ActionResult<List<TimeSeriesPointDto>>> TimeSeries(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId)
    {
        var (f, t) = NormalizeRange(from, to);
        var zone = _zone.Value;

        // Two cohorts: what was opened on a day, and what was closed on it. The closed series used to come from the
        // opened cohort, so anything closed in the range but opened before it was silently dropped.
        var createdDates = await Scoped(f, t, companyId, agentId, departmentId, subBranchId).Select(x => x.CreatedAt).ToListAsync(Ct);
        var closedDates = await ClosedIn(f, t, companyId, agentId, departmentId, subBranchId).Select(x => x.ClosedAtUtc!.Value).ToListAsync(Ct);

        // Days are the business's local calendar days, not UTC days.
        var opened = createdDates.GroupBy(d => BusinessTimeZone.LocalDate(d, zone)).ToDictionary(g => g.Key, g => g.Count());
        var closed = closedDates.GroupBy(d => BusinessTimeZone.LocalDate(d, zone)).ToDictionary(g => g.Key, g => g.Count());

        var points = new List<TimeSeriesPointDto>();
        for (var d = BusinessTimeZone.LocalDate(f, zone); d <= BusinessTimeZone.LocalDate(t, zone); d = d.AddDays(1))
            points.Add(new TimeSeriesPointDto(d, opened.GetValueOrDefault(d), closed.GetValueOrDefault(d)));

        return Ok(points);
    }

    [HttpGet("leaderboard")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> Leaderboard(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId)
    {
        var (f, t) = NormalizeRange(from, to);
        // Ranked by who closed the tickets, among tickets closed in the range. An agent filter narrows the board to
        // that person (it used to be ignored, so the filtered dashboard still ranked everyone).
        var closed = ClosedIn(f, t, companyId, null, departmentId, subBranchId)
            .Where(x => x.Status == TicketStatus.Closed && x.ClosedByUserId.HasValue);
        if (agentId.HasValue) closed = closed.Where(x => x.ClosedByUserId == agentId.Value);

        var ranked = await closed
            .GroupBy(x => x.ClosedByUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(Ct);

        var userIds = ranked.Select(x => x.UserId).ToList();
        var names = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", Ct);

        return Ok(ranked.Select(x => new LeaderboardEntryDto(x.UserId, names.GetValueOrDefault(x.UserId, "Unknown"), x.Count)).ToList());
    }

    /// <summary>Admin-only: ticket counts broken down by branch, for the global cross-branch dashboard view.
    /// New/open are counted on tickets submitted in the range and closed on tickets closed in it, the same cohorts as the summary.</summary>
    [HttpGet("by-branch")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<List<BranchBreakdownEntryDto>>> ByBranch(DateTime? from, DateTime? to)
    {
        var (f, t) = NormalizeRange(from, to);
        var created = await _db.Tickets.AsNoTracking()
            .Where(x => x.CreatedAt >= f && x.CreatedAt <= t && x.DepartmentId.HasValue)
            .Select(x => new { DepartmentId = x.DepartmentId!.Value, x.Status })
            .ToListAsync(Ct);
        var closedPerBranch = await _db.Tickets.AsNoTracking()
            .Where(x => x.ClosedAtUtc != null && x.ClosedAtUtc >= f && x.ClosedAtUtc <= t && x.DepartmentId.HasValue)
            .GroupBy(x => x.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.DepartmentId, g => g.Count, Ct);

        var departments = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, Ct);

        var branchIds = created.Select(x => x.DepartmentId).Concat(closedPerBranch.Keys).Distinct();
        var result = branchIds
            .Select(id => new BranchBreakdownEntryDto(
                id,
                departments.GetValueOrDefault(id, "Unknown"),
                created.Count(x => x.DepartmentId == id && x.Status == TicketStatus.New),
                created.Count(x => x.DepartmentId == id && x.Status is TicketStatus.Open or TicketStatus.InProgress or TicketStatus.Resolved),
                closedPerBranch.GetValueOrDefault(id)))
            .OrderBy(x => x.DepartmentName)
            .ToList();

        return Ok(result);
    }

    /// <summary>Buckets tickets created in range by week/month/quarter/year, so periods can be compared to spot the busiest/quietest ones.</summary>
    [HttpGet("period-comparison")]
    public async Task<ActionResult<List<PeriodComparisonPointDto>>> PeriodComparison(
        DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId, [FromQuery] string groupBy = "month")
    {
        var (f, t) = NormalizeRange(from, to);
        var zone = _zone.Value;
        // Bucket by the business's local date, so a ticket created at 00:30 local on the 1st lands in the new month.
        var createdDates = (await Scoped(f, t, companyId, agentId, departmentId, subBranchId).Select(x => x.CreatedAt).ToListAsync(Ct))
            .Select(d => TimeZoneInfo.ConvertTimeFromUtc(BusinessTimeZone.AsUtc(d), zone))
            .ToList();

        DateTime BucketStart(DateTime d) => groupBy.ToLowerInvariant() switch
        {
            "week" => ISOWeek.ToDateTime(ISOWeek.GetYear(d), ISOWeek.GetWeekOfYear(d), DayOfWeek.Monday),
            "quarter" => new DateTime(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1),
            "year" => new DateTime(d.Year, 1, 1),
            _ => new DateTime(d.Year, d.Month, 1),
        };
        string LabelFor(DateTime bucketStart) => groupBy.ToLowerInvariant() switch
        {
            "week" => $"W{ISOWeek.GetWeekOfYear(bucketStart):00} {bucketStart:yyyy}",
            "quarter" => $"Q{((bucketStart.Month - 1) / 3) + 1} {bucketStart:yyyy}",
            "year" => bucketStart.ToString("yyyy"),
            _ => bucketStart.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        };

        var points = createdDates
            .GroupBy(BucketStart)
            .Select(g => new PeriodComparisonPointDto(LabelFor(g.Key), g.Key, g.Count()))
            .OrderBy(x => x.PeriodStart)
            .ToList();

        return Ok(points);
    }

    /// <summary>Ranks how many tickets fell under each help topic in the range — the clearest signal of what's causing the most trouble.</summary>
    [HttpGet("top-issues")]
    public async Task<ActionResult<List<TopIssueDto>>> TopIssues(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId, [FromQuery] int limit = 10)
    {
        var (f, t) = NormalizeRange(from, to);
        var grouped = await Scoped(f, t, companyId, agentId, departmentId, subBranchId)
            .Where(x => x.HelpTopicId.HasValue)
            .GroupBy(x => x.HelpTopicId!.Value)
            .Select(g => new { HelpTopicId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(Ct);

        var topicIds = grouped.Select(g => g.HelpTopicId).ToList();
        var topicNames = await _db.HelpTopics.Where(h => topicIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Name, Ct);

        return Ok(grouped.Select(g => new TopIssueDto(topicNames.GetValueOrDefault(g.HelpTopicId, "Unknown"), g.Count)).ToList());
    }

    /// <summary>
    /// AI-generated qualitative summary of what went wrong most in a period (min. 1 month), grounded strictly
    /// in the help-topic breakdown for that period — no general knowledge, no invented details.
    /// </summary>
    [HttpPost("ai-insights")]
    public async Task<ActionResult<AiInsightsResponseDto>> AiInsights(AiInsightsRequest request)
    {
        if ((request.To - request.From).TotalDays < 28)
            return BadRequest(new { message = "Select a period of at least one month for AI analysis." });

        var raw = await Scoped(request.From, request.To, request.CompanyId, request.AgentId, request.DepartmentId, request.SubBranchId)
            .Select(x => new { x.Title, x.HelpTopicId, x.Status })
            .ToListAsync(Ct);

        if (raw.Count == 0)
            return Ok(new AiInsightsResponseDto("No tickets found in this period to analyze."));

        var topicNames = await _db.HelpTopics.ToDictionaryAsync(h => h.Id, h => h.Name, Ct);

        var byTopic = raw.Where(x => x.HelpTopicId.HasValue)
            .GroupBy(x => x.HelpTopicId!.Value)
            .Select(g => new { Name = topicNames.GetValueOrDefault(g.Key, "Unknown"), Count = g.Count(), Samples = g.Take(5).Select(x => x.Title).ToList() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        var context = new StringBuilder();
        context.AppendLine($"Total tickets in period: {raw.Count} (period: {request.From:yyyy-MM-dd} to {request.To:yyyy-MM-dd})");
        foreach (var topic in byTopic)
            context.AppendLine($"- {topic.Name}: {topic.Count} tickets. Example titles: {string.Join("; ", topic.Samples)}");

        var answer = await _chatCompletionService.AskAsync(
            InsightsSystemInstruction, context.ToString(), "Which problems occurred most in this period and what should we do about them?", Ct);

        return Ok(new AiInsightsResponseDto(answer ?? "AI analysis isn't configured yet (missing Google AI key)."));
    }

    /// <summary>
    /// Downloads the filtered ticket list as an Excel workbook — a "Tickets" sheet with the raw data and a
    /// "Summary" sheet with key stats plus a monthly bar chart. Respects the same branch/company/agent/date
    /// scoping as the other report endpoints.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(DateTime? from, DateTime? to, Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId)
    {
        var (f, t) = NormalizeRange(from, to);
        var tickets = await Scoped(f, t, companyId, agentId, departmentId, subBranchId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(Ct);

        var userIds = tickets.SelectMany(x => new[] { x.ClientId, x.AssignedToUserId, x.ClosedByUserId })
            .Where(id => id.HasValue).Select(id => id!.Value)
            .Concat(tickets.Select(x => x.ClientId)).Distinct().ToList();
        var names = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", Ct);
        var companies = await _db.Companies.ToDictionaryAsync(c => c.Id, c => c.Name, Ct);
        var departments = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, Ct);

        var rows = tickets.Select(x => new TicketExportRow(
            x.TicketNumber,
            x.Title,
            x.Status.ToString(),
            x.DepartmentId.HasValue ? departments.GetValueOrDefault(x.DepartmentId.Value, "") : "",
            companies.GetValueOrDefault(x.CompanyId, ""),
            names.GetValueOrDefault(x.ClientId, ""),
            x.AssignedToUserId.HasValue ? names.GetValueOrDefault(x.AssignedToUserId.Value) : null,
            x.CreatedAt,
            x.OpenedAtUtc,
            x.ClosedAtUtc,
            x.OpenedAtUtc.HasValue && x.ClosedAtUtc.HasValue ? Math.Round((x.ClosedAtUtc.Value - x.OpenedAtUtc.Value).TotalHours, 1) : (double?)null,
            x.ClosedByUserId.HasValue ? names.GetValueOrDefault(x.ClosedByUserId.Value) : null
        )).ToList();

        var timelineRows = await BuildTimelineExportRowsAsync(f, t, agentId, departmentId);

        var bytes = TicketExcelExporter.Build(rows, f, t, timelineRows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"tickets-export-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    /// <summary>
    /// One row per ticket/task work block for every Employee in scope (a specific agent, a whole branch, or —
    /// with no filter — every Employee) across the exported date range. Feeds the export's "Timeline" sheet,
    /// the data-table equivalent of the Dashboard's Gantt chart for the whole team at once.
    /// </summary>
    private async Task<List<TimelineExportRow>> BuildTimelineExportRowsAsync(DateTime from, DateTime to, Guid? agentId, Guid? departmentId)
    {
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var employeeQuery = _db.Users.AsNoTracking().Where(u => u.Role == UserRole.Employee && u.IsActive);
        // Same isolation as Scoped(): a non-admin only ever gets their own branch, whatever agent or branch was requested.
        if (!isAdmin) employeeQuery = employeeQuery.Where(u => u.DepartmentId == _currentUser.DepartmentId);
        if (agentId.HasValue) employeeQuery = employeeQuery.Where(u => u.Id == agentId.Value);
        else if (isAdmin && departmentId.HasValue) employeeQuery = employeeQuery.Where(u => u.DepartmentId == departmentId.Value);
        var employees = await employeeQuery.OrderBy(u => u.FirstName).ToListAsync(Ct);

        var timelineRows = new List<TimelineExportRow>();
        foreach (var employee in employees)
        {
            var employeeName = $"{employee.FirstName} {employee.LastName}";

            // Same blocks as the Gantt chart. Hours are only given for logged work; the stand-in for a ticket nobody logged time on
            // would otherwise report "opened to closed" as if it were effort.
            var ticketBlocks = await Queries.TicketWorkBlocks.ForUserAsync(_db, employee.Id, from, to, isAdmin ? null : _currentUser.DepartmentId, Ct);
            foreach (var b in ticketBlocks)
            {
                timelineRows.Add(new TimelineExportRow(
                    employeeName, "Ticket", $"#{b.TicketNumber} — {b.Title}", b.Status.ToString(), b.Start, b.End,
                    b.IsLogged && b.End.HasValue ? Math.Round((b.End.Value - b.Start).TotalHours, 1) : (double?)null));
            }

            var taskBlocks = await _db.WorkTasks.AsNoTracking()
                .Where(x => x.AssignedToUserId == employee.Id && x.StartedAtUtc != null && x.StartedAtUtc < to && (x.EndedAtUtc == null || x.EndedAtUtc >= from))
                .Select(x => new { x.Title, x.Status, x.StartedAtUtc, x.EndedAtUtc })
                .ToListAsync(Ct);
            foreach (var b in taskBlocks)
            {
                timelineRows.Add(new TimelineExportRow(
                    employeeName, "Task", b.Title, b.Status.ToString(), b.StartedAtUtc, b.EndedAtUtc,
                    b.StartedAtUtc.HasValue && b.EndedAtUtc.HasValue ? Math.Round((b.EndedAtUtc.Value - b.StartedAtUtc.Value).TotalHours, 1) : (double?)null));
            }
        }

        return timelineRows.OrderBy(r => r.Employee).ThenBy(r => r.StartUtc).ToList();
    }

    private static (DateTime From, DateTime To) NormalizeRange(DateTime? from, DateTime? to)
    {
        var t = BusinessTimeZone.AsUtc(to ?? DateTime.UtcNow);
        var f = from.HasValue ? BusinessTimeZone.AsUtc(from.Value) : t.AddDays(-30);
        return (f, t);
    }
}
