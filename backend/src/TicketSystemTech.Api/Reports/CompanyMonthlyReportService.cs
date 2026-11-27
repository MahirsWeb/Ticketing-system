using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Reporting;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Reports;

/// <summary>
/// The monthly report of one company: how many tickets, how they ended, how long, the SLA, the hours, and the list. It can go to the client or to the archive, so it
/// comes in two versions: <c>internal</c> (everything, with the internal category and the hours) and <c>client</c> (no internal category, no names of staff, and the hours
/// only when asked for). Internal notes are in neither. Read only. The figures are for all branches, whoever asks; the list of tickets is limited to the branch of an
/// employee, and says so.
/// </summary>
public interface ICompanyMonthlyReportService
{
    /// <summary>For the screen: the list of tickets is cut to <paramref name="limit"/> (at most <see cref="CompanyMonthlyReportService.MostTickets"/>); the figures never are. Without a limit the list is whole (the xlsx).</summary>
    Task<Outcome<CompanyMonthlyDto>> ReportAsync(Guid companyId, int year, int month, string? audience, bool? includeHours, int? limit, CancellationToken ct);

    Task<Outcome<ExportFile>> ExportAsync(Guid companyId, int year, int month, string? audience, bool? includeHours, string? language, CancellationToken ct);
}

public sealed class CompanyMonthlyReportService : ICompanyMonthlyReportService
{
    public const string Internal = "internal";
    public const string Client = "client";
    public const int MostTickets = 1000;
    public const string ApplicationName = "Tix";

    private static readonly int[] TicketColumns = { 18, 44, 16, 18, 18, 16 };

    private static readonly TicketStatus[] StatusOrder = { TicketStatus.New, TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly Lazy<TimeZoneInfo> _zone;

    public CompanyMonthlyReportService(AppDbContext db, ICurrentUserService currentUser, IOptions<SlaOptions> sla, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _time = time;
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(sla.Value.TimeZoneId));
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<Outcome<CompanyMonthlyDto>> ReportAsync(Guid companyId, int year, int month, string? audience, bool? includeHours, int? limit, CancellationToken ct)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100) return Outcome<CompanyMonthlyDto>.Invalid("The month is not valid.");
        var kind = string.IsNullOrWhiteSpace(audience) ? Internal : audience.Trim().ToLowerInvariant();
        if (kind is not (Internal or Client)) return Outcome<CompanyMonthlyDto>.Invalid("The version must be internal or client.");
        var company = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
        if (company is null) return Outcome<CompanyMonthlyDto>.NotFound();

        var now = UtcNow;
        var (start, end) = BusinessMonth.Window(year, month, _zone.Value);
        // The internal version always has the hours; the client version only when they are asked for.
        var withHours = kind == Internal || includeHours == true;

        var all = _db.Tickets.AsNoTracking().Where(t => t.CompanyId == companyId);
        var created = all.Where(t => t.CreatedAt >= start && t.CreatedAt < end);
        var closed = all.Where(t => t.ClosedAtUtc != null && t.ClosedAtUtc >= start && t.ClosedAtUtc < end);

        var byStatus = await created.GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, ct);
        var topics = await created.GroupBy(t => t.HelpTopicId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var categories = kind == Internal ? await created.GroupBy(t => t.CategoryId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct) : null;
        var topicNames = await _db.HelpTopics.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h.Name, ct);
        var categoryNames = await _db.TicketCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Time to solve and the SLA: the tickets closed in the month, as the overview counts them.
        var closedRows = await closed.Select(t => new { t.OpenedAtUtc, t.ClosedAtUtc, t.DueDateUtc }).ToListAsync(ct);
        var hours = closedRows.Where(r => r.OpenedAtUtc.HasValue && r.ClosedAtUtc.HasValue).Select(r => (r.ClosedAtUtc!.Value - r.OpenedAtUtc!.Value).TotalHours).ToList();
        var distribution = Distribution.Of(hours);
        var withDue = closedRows.Where(r => r.DueDateUtc.HasValue).ToList();
        double? sla = withDue.Count > 0 ? Math.Round((double)withDue.Count(r => r.ClosedAtUtc <= r.DueDateUtc) / withDue.Count * 100, 1) : null;

        double? worked = null;
        if (withHours)
        {
            var sessions = await _db.TicketWorkSessions.AsNoTracking()
                .Where(s => s.Ticket!.CompanyId == companyId && s.StartedAtUtc >= start && s.StartedAtUtc < end)
                .Select(s => new { s.StartedAtUtc, s.EndedAtUtc }).ToListAsync(ct);
            var seconds = sessions.Sum(s => WorkTimeSummary.Seconds(new WorkEntry(Guid.Empty, Guid.Empty, false, Guid.Empty, "", "", null, null, s.StartedAtUtc, s.EndedAtUtc), now));
            worked = WorkTimeSummary.Hours(seconds);
        }

        // The list: created or closed in the month. An employee gets only the tickets of their own branch, and the document says so.
        var listed = all.Where(t => (t.CreatedAt >= start && t.CreatedAt < end) || (t.ClosedAtUtc != null && t.ClosedAtUtc >= start && t.ClosedAtUtc < end));
        var scope = "all";
        if (_currentUser.Role != UserRole.Admin)
        {
            var own = _currentUser.DepartmentId;
            listed = listed.Where(t => t.DepartmentId == own);
            scope = own is { } d ? await _db.Departments.AsNoTracking().Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "?" : "?";
        }
        var total = await listed.CountAsync(ct);
        var ordered = listed.OrderBy(t => t.CreatedAt).ThenBy(t => t.TicketNumber);
        var shown = limit.HasValue ? ordered.Take(Math.Clamp(limit.Value, 1, MostTickets)) : ordered; // the workbook is an archive: it has them all
        var rows = await shown
            .Select(t => new { t.Id, t.TicketNumber, t.Title, t.Status, t.CreatedAt, t.OpenedAtUtc, t.ClosedAtUtc }).ToListAsync(ct);
        var tickets = rows.Select(r => new CompanyMonthlyTicketDto(r.Id, r.TicketNumber, r.Title, r.Status, r.CreatedAt, r.ClosedAtUtc,
            r.OpenedAtUtc.HasValue && r.ClosedAtUtc.HasValue ? Math.Round((r.ClosedAtUtc.Value - r.OpenedAtUtc.Value).TotalHours, 1) : null)).ToList();

        var makerId = _currentUser.UserId;
        // The client version has no name of staff in it, not even of the person who made it: it says it was made by the application.
        var madeBy = kind == Client ? ApplicationName
            : await _db.Users.AsNoTracking().Where(u => u.Id == makerId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct) ?? "";

        List<NamedCountDto> Named(IEnumerable<(Guid? Id, int Count)> groups, Dictionary<Guid, string> names, string none) =>
            groups.OrderByDescending(g => g.Count).ThenBy(g => g.Id.HasValue ? names.GetValueOrDefault(g.Id.Value, "") : "￿")
                .Select(g => new NamedCountDto(g.Id, g.Id is { } id ? names.GetValueOrDefault(id, "?") : none, g.Count)).ToList();

        var header = new CompanyMonthlyHeaderDto(company.Id, company.Name, year, month, kind, withHours, now, madeBy, scope);
        return Outcome<CompanyMonthlyDto>.Ok(new CompanyMonthlyDto(
            header,
            await created.CountAsync(ct),
            closedRows.Count,
            StatusOrder.Select(s => new StatusCountDto(s.ToString(), byStatus.GetValueOrDefault(s))).ToList(),
            Named(topics.Select(g => (g.Id, g.Count)), topicNames, ""),
            categories is null ? null : Named(categories.Select(g => (g.Id, g.Count)), categoryNames, ""),
            distribution.Count,
            Math.Round(distribution.Average, 1),
            Math.Round(distribution.Median, 1),
            sla,
            withDue.Count,
            worked,
            tickets,
            total,
            total > tickets.Count));
    }

    // ---------------------------------------------------------------- the workbook

    public async Task<Outcome<ExportFile>> ExportAsync(Guid companyId, int year, int month, string? audience, bool? includeHours, string? language, CancellationToken ct)
    {
        var outcome = await ReportAsync(companyId, year, month, audience, includeHours, null, ct);
        if (!outcome.Succeeded) return outcome.Failure switch
        {
            Failure.NotFound => Outcome<ExportFile>.NotFound(),
            Failure.Forbidden => Outcome<ExportFile>.Forbidden(),
            _ => Outcome<ExportFile>.Invalid(outcome.Message ?? "")
        };
        var report = outcome.Value!;
        var h = report.Header;
        var makerId = _currentUser.UserId;
        var preferred = await _db.Users.AsNoTracking().Where(u => u.Id == makerId).Select(u => u.PreferredLanguage).FirstOrDefaultAsync(ct);
        var t = ReportText.For(language is null ? preferred : EmailLanguage.Normalize(language));
        var zone = _zone.Value;
        string Local(DateTime? utc) => utc is { } v ? TimeZoneInfo.ConvertTimeFromUtc(BusinessTimeZone.AsUtc(v), zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : "";

        var header = new List<(string, string)>
        {
            (t("cm_title"), h.Company),
            (t("ts_month"), $"{h.Month:00}.{h.Year}"),
            (t("cm_version"), h.Audience == Internal ? t("cm_versionInternal") : t("cm_versionClient")),
            (t("ts_madeBy"), h.MadeBy),
            (t("ts_madeAt"), Local(h.MadeAtUtc) + " (" + zone.Id + ")"),
            (t("ts_snapshotLabel"), t("doc_snapshot")),
        };
        header.Add((t("cm_scopeLabel"), h.ListScope == "all" ? t("cm_scopeAll") : string.Format(CultureInfo.InvariantCulture, t("cm_scopeBranch"), h.ListScope)));
        if (h.Audience == Client) header.Add((t("cm_clientNoteLabel"), t("cm_clientNote")));

        var figures = new List<object?[]>
        {
            new object?[] { t("cm_created"), report.Created },
            new object?[] { t("cm_closed"), report.Closed },
            new object?[] { t("cm_avgResolution"), report.AverageResolutionHours },
            new object?[] { t("cm_medianResolution"), report.MedianResolutionHours },
            new object?[] { t("cm_resolutionCount"), report.ResolutionCount },
            new object?[] { t("cm_sla"), report.SlaComplianceRate is { } r ? r.ToString("0.0", CultureInfo.InvariantCulture) + " %" : t("cm_noSla") },
        };
        if (report.WorkHours is { } worked) figures.Add(new object?[] { t("cm_workHours"), worked });

        var distribution = new List<DocTable>
        {
            new(new[] { t("tc_status"), t("cm_count") }, report.ByStatus.Select(s => new object?[] { t("st_" + s.Status), s.Count }).ToList()),
            new(new[] { t("cm_topic"), t("cm_count") }, report.ByTopic.Select(s => new object?[] { s.Id is null ? t("cm_none") : s.Name, s.Count }).ToList()),
        };
        if (report.ByCategory is { } categories)
            distribution.Add(new(new[] { t("cm_category"), t("cm_count") }, categories.Select(s => new object?[] { s.Id is null ? t("cm_none") : s.Name, s.Count }).ToList()));

        var list = report.Tickets.Select(x => new object?[] { x.TicketNumber, x.Title, t("st_" + x.Status), Local(x.CreatedAt), Local(x.ClosedAtUtc), x.ResolutionHours }).ToList();
        var sheets = new List<DocSheet>
        {
            new(t("cm_sheetSummary"), header, new List<DocTable> { new(new[] { t("tc_field"), t("tc_value") }, figures) }),
            new(t("cm_sheetDistribution"), header, distribution),
            new(t("cm_sheetTickets"), header, new List<DocTable> { new(new[] { t("tc_ticket"), t("cm_ticketTitle"), t("tc_status"), t("tc_created"), t("tc_closed"), t("tc_resolution") }, list) }, TicketColumns),
        };

        var name = new string(h.Company.Normalize(System.Text.NormalizationForm.FormD).Where(c => char.IsAsciiLetterOrDigit(c)).ToArray()).ToLowerInvariant();
        return Outcome<ExportFile>.Ok(new ExportFile(DocumentWorkbook.Build(sheets), $"company-report-{(name.Length == 0 ? "company" : name)}-{h.Year}-{h.Month:00}-{h.Audience}.xlsx"));
    }
}
