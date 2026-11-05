using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Reporting;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Reports;

/// <summary>What every report may be narrowed by. A missing date range means the last 30 days; a branch filter only narrows for Admin (staff always get their own branch).</summary>
public sealed record ReportFilter(DateTime? From, DateTime? To, Guid? CompanyId, Guid? AgentId, Guid? DepartmentId, Guid? SubBranchId);

/// <summary>The Excel workbook of the export and the name it should be offered under.</summary>
public sealed record ExportFile(byte[] Content, string FileName);

/// <summary>The figures behind the dashboard and the export. Branches are isolated: a non-admin only ever sees their own branch's data, whatever is requested.</summary>
public interface IReportService
{
    Task<ReportSummaryDto> SummaryAsync(ReportFilter filter, CancellationToken ct);

    Task<List<TimeSeriesPointDto>> TimeSeriesAsync(ReportFilter filter, CancellationToken ct);

    Task<List<LeaderboardEntryDto>> LeaderboardAsync(ReportFilter filter, CancellationToken ct);

    /// <summary>Admin-only: ticket counts broken down by branch, for the global cross-branch dashboard view.</summary>
    Task<List<BranchBreakdownEntryDto>> ByBranchAsync(DateTime? from, DateTime? to, CancellationToken ct);

    /// <summary>Buckets tickets created in range by week/month/quarter/year, so periods can be compared to spot the busiest/quietest ones.</summary>
    Task<List<PeriodComparisonPointDto>> PeriodComparisonAsync(ReportFilter filter, string groupBy, CancellationToken ct);

    /// <summary>Ranks how many tickets fell under each help topic in the range.</summary>
    Task<List<TopIssueDto>> TopIssuesAsync(ReportFilter filter, int limit, CancellationToken ct);

    /// <summary>AI-generated qualitative summary of what went wrong most in a period (at least a month), grounded strictly in the help-topic breakdown for that period.</summary>
    Task<Outcome<AiInsightsResponseDto>> AiInsightsAsync(AiInsightsRequest request, CancellationToken ct);

    /// <summary>The filtered ticket list as an Excel workbook: a "Tickets" sheet with the raw data, a "Summary" sheet with key stats and a monthly chart, and a "Timeline" sheet.</summary>
    Task<ExportFile> ExportAsync(ReportFilter filter, CancellationToken ct);
}

public sealed class ReportService : IReportService
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
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public ReportService(AppDbContext db, ICurrentUserService currentUser, IChatCompletionService chatCompletionService, IOptions<SlaOptions> slaOptions, TimeProvider time)
    {
        _time = time;
        _db = db;
        _currentUser = currentUser;
        _chatCompletionService = chatCompletionService;
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(slaOptions.Value.TimeZoneId));
    }

    /// <summary>The branch/company/agent scope shared by every report, with no date restriction.</summary>
    private IQueryable<Ticket> InScope(Guid? companyId, Guid? agentId, Guid? departmentId, Guid? subBranchId)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();

        // "Tickets of agent X" means the ones they are on (as the main assignee or as help); the ticket list works the same way.
        if (agentId.HasValue)
            query = query.Where(t => t.Assignees.Any(a => a.UserId == agentId.Value));

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

    public async Task<ReportSummaryDto> SummaryAsync(ReportFilter filter, CancellationToken ct)
    {
        var (from, to, companyId, agentId, departmentId, subBranchId) = filter;
        var (f, t) = NormalizeRange(from, to);

        var createdStatuses = await Scoped(f, t, companyId, agentId, departmentId, subBranchId).Select(x => x.Status).ToListAsync(ct);
        var totalNew = createdStatuses.Count(x => x == TicketStatus.New);
        // Resolved is not closed yet, so it is still work in progress.
        var totalOpen = createdStatuses.Count(x => x is TicketStatus.Open or TicketStatus.InProgress or TicketStatus.Resolved);

        var closed = await ClosedIn(f, t, companyId, agentId, departmentId, subBranchId)
            .Select(x => new { x.OpenedAtUtc, x.ClosedAtUtc, x.DueDateUtc })
            .ToListAsync(ct);

        var resolutionHours = closed
            .Where(x => x.OpenedAtUtc.HasValue && x.ClosedAtUtc.HasValue)
            .Select(x => (x.ClosedAtUtc!.Value - x.OpenedAtUtc!.Value).TotalHours)
            .ToList();
        var avgResolution = resolutionHours.Count > 0 ? resolutionHours.Average() : 0;

        var closedWithDueDate = closed.Where(x => x.DueDateUtc.HasValue).ToList();
        var compliant = closedWithDueDate.Count(x => x.ClosedAtUtc <= x.DueDateUtc);
        // No closed tickets with a due date means there is nothing to measure, which must not read as perfect compliance.
        double? slaRate = closedWithDueDate.Count > 0 ? Math.Round((double)compliant / closedWithDueDate.Count * 100, 1) : null;

        return new ReportSummaryDto(totalNew, totalOpen, closed.Count, Math.Round(avgResolution, 1), slaRate);
    }

    public async Task<List<TimeSeriesPointDto>> TimeSeriesAsync(ReportFilter filter, CancellationToken ct)
    {
        var (from, to, companyId, agentId, departmentId, subBranchId) = filter;
        var (f, t) = NormalizeRange(from, to);
        var zone = _zone.Value;

        // Two cohorts: what was opened on a day, and what was closed on it. The closed series used to come from the
        // opened cohort, so anything closed in the range but opened before it was silently dropped.
        var createdDates = await Scoped(f, t, companyId, agentId, departmentId, subBranchId).Select(x => x.CreatedAt).ToListAsync(ct);
        var closedDates = await ClosedIn(f, t, companyId, agentId, departmentId, subBranchId).Select(x => x.ClosedAtUtc!.Value).ToListAsync(ct);

        // Days are the business's local calendar days, not UTC days.
        var opened = createdDates.GroupBy(d => BusinessTimeZone.LocalDate(d, zone)).ToDictionary(g => g.Key, g => g.Count());
        var closed = closedDates.GroupBy(d => BusinessTimeZone.LocalDate(d, zone)).ToDictionary(g => g.Key, g => g.Count());

        var points = new List<TimeSeriesPointDto>();
        for (var d = BusinessTimeZone.LocalDate(f, zone); d <= BusinessTimeZone.LocalDate(t, zone); d = d.AddDays(1))
            points.Add(new TimeSeriesPointDto(d, opened.GetValueOrDefault(d), closed.GetValueOrDefault(d)));

        return points;
    }

    public async Task<List<LeaderboardEntryDto>> LeaderboardAsync(ReportFilter filter, CancellationToken ct)
    {
        var (from, to, companyId, agentId, departmentId, subBranchId) = filter;
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
            .ToListAsync(ct);

        var userIds = ranked.Select(x => x.UserId).ToList();
        var names = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", ct);

        return ranked.Select(x => new LeaderboardEntryDto(x.UserId, names.GetValueOrDefault(x.UserId, "Unknown"), x.Count)).ToList();
    }

    public async Task<List<BranchBreakdownEntryDto>> ByBranchAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (f, t) = NormalizeRange(from, to);
        var created = await _db.Tickets.AsNoTracking()
            .Where(x => x.CreatedAt >= f && x.CreatedAt <= t && x.DepartmentId.HasValue)
            .Select(x => new { DepartmentId = x.DepartmentId!.Value, x.Status })
            .ToListAsync(ct);
        var closedPerBranch = await _db.Tickets.AsNoTracking()
            .Where(x => x.ClosedAtUtc != null && x.ClosedAtUtc >= f && x.ClosedAtUtc <= t && x.DepartmentId.HasValue)
            .GroupBy(x => x.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.DepartmentId, g => g.Count, ct);

        var departments = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, ct);

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

        return result;
    }

    public async Task<List<PeriodComparisonPointDto>> PeriodComparisonAsync(ReportFilter filter, string groupBy, CancellationToken ct)
    {
        var (from, to, companyId, agentId, departmentId, subBranchId) = filter;
        var (f, t) = NormalizeRange(from, to);
        var zone = _zone.Value;
        // Bucket by the business's local date, so a ticket created at 00:30 local on the 1st lands in the new month.
        var createdDates = (await Scoped(f, t, companyId, agentId, departmentId, subBranchId).Select(x => x.CreatedAt).ToListAsync(ct))
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
            "week" => string.Create(CultureInfo.InvariantCulture, $"W{ISOWeek.GetWeekOfYear(bucketStart):00} {bucketStart:yyyy}"),
            "quarter" => string.Create(CultureInfo.InvariantCulture, $"Q{((bucketStart.Month - 1) / 3) + 1} {bucketStart:yyyy}"),
            "year" => bucketStart.ToString("yyyy", CultureInfo.InvariantCulture),
            _ => bucketStart.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        };

        var points = createdDates
            .GroupBy(BucketStart)
            .Select(g => new PeriodComparisonPointDto(LabelFor(g.Key), g.Key, g.Count()))
            .OrderBy(x => x.PeriodStart)
            .ToList();

        return points;
    }

    public async Task<List<TopIssueDto>> TopIssuesAsync(ReportFilter filter, int limit, CancellationToken ct)
    {
        var (from, to, companyId, agentId, departmentId, subBranchId) = filter;
        var (f, t) = NormalizeRange(from, to);
        var grouped = await Scoped(f, t, companyId, agentId, departmentId, subBranchId)
            .Where(x => x.HelpTopicId.HasValue)
            .GroupBy(x => x.HelpTopicId!.Value)
            .Select(g => new { HelpTopicId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(ct);

        var topicIds = grouped.Select(g => g.HelpTopicId).ToList();
        var topicNames = await _db.HelpTopics.Where(h => topicIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        return grouped.Select(g => new TopIssueDto(topicNames.GetValueOrDefault(g.HelpTopicId, "Unknown"), g.Count)).ToList();
    }

    public async Task<Outcome<AiInsightsResponseDto>> AiInsightsAsync(AiInsightsRequest request, CancellationToken ct)
    {
        if ((request.To - request.From).TotalDays < 28)
            return Outcome<AiInsightsResponseDto>.Invalid("Select a period of at least one month for AI analysis.");

        var raw = await Scoped(request.From, request.To, request.CompanyId, request.AgentId, request.DepartmentId, request.SubBranchId)
            .Select(x => new { x.Title, x.HelpTopicId, x.Status })
            .ToListAsync(ct);

        if (raw.Count == 0)
            return Outcome<AiInsightsResponseDto>.Ok(new AiInsightsResponseDto("No tickets found in this period to analyze."));

        var topicNames = await _db.HelpTopics.ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        var byTopic = raw.Where(x => x.HelpTopicId.HasValue)
            .GroupBy(x => x.HelpTopicId!.Value)
            .Select(g => new { Name = topicNames.GetValueOrDefault(g.Key, "Unknown"), Count = g.Count(), Samples = g.Take(5).Select(x => x.Title).ToList() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        var context = new StringBuilder();
        context.AppendLine(CultureInfo.InvariantCulture, $"Total tickets in period: {raw.Count} (period: {request.From:yyyy-MM-dd} to {request.To:yyyy-MM-dd})");
        foreach (var topic in byTopic)
            context.AppendLine(CultureInfo.InvariantCulture, $"- {topic.Name}: {topic.Count} tickets. Example titles: {string.Join("; ", topic.Samples)}");

        var answer = await _chatCompletionService.AskAsync(
            InsightsSystemInstruction, context.ToString(), "Which problems occurred most in this period and what should we do about them?", ct);

        return Outcome<AiInsightsResponseDto>.Ok(new AiInsightsResponseDto(answer ?? (_chatCompletionService.IsAvailable
            ? "The AI did not answer this time. Please try again."
            : "AI is switched off or not configured on this installation.")));
    }

    public async Task<ExportFile> ExportAsync(ReportFilter filter, CancellationToken ct)
    {
        var (from, to, companyId, agentId, departmentId, subBranchId) = filter;
        var (f, t) = NormalizeRange(from, to);
        var tickets = await Scoped(f, t, companyId, agentId, departmentId, subBranchId)
            .Include(x => x.Assignees)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        var userIds = tickets.SelectMany(x => new[] { x.ClientId, x.PrimaryAssigneeId, x.ClosedByUserId })
            .Where(id => id.HasValue).Select(id => id!.Value)
            .Concat(tickets.Select(x => x.ClientId)).Distinct().ToList();
        var names = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", ct);
        var companies = await _db.Companies.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var departments = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var rows = tickets.Select(x => new TicketExportRow(
            x.TicketNumber,
            x.Title,
            x.Status.ToString(),
            x.DepartmentId.HasValue ? departments.GetValueOrDefault(x.DepartmentId.Value, "") : "",
            companies.GetValueOrDefault(x.CompanyId, ""),
            names.GetValueOrDefault(x.ClientId, ""),
            x.PrimaryAssigneeId.HasValue ? names.GetValueOrDefault(x.PrimaryAssigneeId.Value) : null,
            x.CreatedAt,
            x.OpenedAtUtc,
            x.ClosedAtUtc,
            x.OpenedAtUtc.HasValue && x.ClosedAtUtc.HasValue ? Math.Round((x.ClosedAtUtc.Value - x.OpenedAtUtc.Value).TotalHours, 1) : (double?)null,
            x.ClosedByUserId.HasValue ? names.GetValueOrDefault(x.ClosedByUserId.Value) : null
        )).ToList();

        var timelineRows = await BuildTimelineExportRowsAsync(f, t, agentId, departmentId, ct);

        var bytes = TicketExcelExporter.Build(rows, f, t, timelineRows);
        return new ExportFile(bytes, $"tickets-export-{UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    /// <summary>
    /// One row per ticket/task work block for every Employee in scope (a specific agent, a whole branch, or —
    /// with no filter — every Employee) across the exported date range. Feeds the export's "Timeline" sheet,
    /// the data-table equivalent of the Dashboard's Gantt chart for the whole team at once.
    /// </summary>
    private async Task<List<TimelineExportRow>> BuildTimelineExportRowsAsync(DateTime from, DateTime to, Guid? agentId, Guid? departmentId, CancellationToken ct)
    {
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var employeeQuery = _db.Users.AsNoTracking().Where(u => u.Role == UserRole.Employee && u.IsActive);
        // Same isolation as Scoped(): a non-admin only ever gets their own branch, whatever agent or branch was requested.
        if (!isAdmin) employeeQuery = employeeQuery.Where(u => u.DepartmentId == _currentUser.DepartmentId);
        if (agentId.HasValue) employeeQuery = employeeQuery.Where(u => u.Id == agentId.Value);
        else if (isAdmin && departmentId.HasValue) employeeQuery = employeeQuery.Where(u => u.DepartmentId == departmentId.Value);
        var employees = await employeeQuery.OrderBy(u => u.FirstName).ToListAsync(ct);

        var timelineRows = new List<TimelineExportRow>();
        foreach (var employee in employees)
        {
            var employeeName = $"{employee.FirstName} {employee.LastName}";

            // Same blocks as the Gantt chart. Hours are only given for logged work; the stand-in for a ticket nobody logged time on
            // would otherwise report "opened to closed" as if it were effort.
            var ticketBlocks = await Queries.TicketWorkBlocks.ForUserAsync(_db, employee.Id, from, to, isAdmin ? null : _currentUser.DepartmentId, UtcNow, ct);
            foreach (var b in ticketBlocks)
            {
                timelineRows.Add(new TimelineExportRow(
                    employeeName, "Ticket", $"#{b.TicketNumber} — {b.Title}", b.Status.ToString(), b.Start, b.End,
                    b.IsLogged && b.End.HasValue ? Math.Round((b.End.Value - b.Start).TotalHours, 1) : (double?)null));
            }

            // One row per stretch of work, as on the chart.
            var now = UtcNow;
            var taskBlocks = await _db.WorkTaskSessions.AsNoTracking()
                .Where(x => x.UserId == employee.Id && x.StartedAtUtc < to && (x.EndedAtUtc ?? now) >= from)
                .Select(x => new { x.WorkTask!.Title, x.StartedAtUtc, x.EndedAtUtc })
                .ToListAsync(ct);
            foreach (var b in taskBlocks)
            {
                timelineRows.Add(new TimelineExportRow(
                    employeeName, "Task", b.Title, b.EndedAtUtc.HasValue ? "Done" : "InProgress", b.StartedAtUtc, b.EndedAtUtc,
                    b.EndedAtUtc.HasValue ? Math.Round((b.EndedAtUtc.Value - b.StartedAtUtc).TotalHours, 1) : (double?)null));
            }
        }

        return timelineRows.OrderBy(r => r.Employee).ThenBy(r => r.StartUtc).ToList();
    }

    private (DateTime From, DateTime To) NormalizeRange(DateTime? from, DateTime? to)
    {
        var t = BusinessTimeZone.AsUtc(to ?? UtcNow);
        var f = from.HasValue ? BusinessTimeZone.AsUtc(from.Value) : t.AddDays(-30);
        return (f, t);
    }
}
