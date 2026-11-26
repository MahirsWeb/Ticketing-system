using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Reporting;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Reports;

/// <summary>
/// What is asked for: a period, and either nothing (the companies that sent the most tickets), a company, or a company and one client of it. A client is
/// never asked for without a company: the person is chosen from the people of a company.
/// </summary>
public sealed record CompanyReportFilter(DateTime? From, DateTime? To, Guid? CompanyId, Guid? ClientId);

/// <summary>
/// The statistics of the companies that sent tickets, for the second tab of the overview. Unlike <see cref="IReportService"/> it is NOT limited to the branch of
/// the person asking: staff of every branch get the same figures, for all branches together. That is a deliberate, narrow exception to the isolation of branches:
/// only totals, the names of companies, of topics, of categories, of branches and of the people who reported or closed tickets are given, never a ticket, a title
/// or a text. The list of tickets and the old reports stay isolated by branch.
/// </summary>
public interface ICompanyReportService
{
    /// <summary>The companies with the most tickets created in the period (at most 10), and what part of all tickets they make.</summary>
    Task<TopCompaniesDto> TopCompaniesAsync(CompanyReportFilter filter, int limit, CancellationToken ct);

    /// <summary>All companies, one page of them, searched by name and sorted by what is asked.</summary>
    Task<CompanyListPageDto> ListCompaniesAsync(CompanyReportFilter filter, string? search, string? sort, bool descending, int page, int pageSize, CancellationToken ct);

    Task<Outcome<CompanySummaryDto>> SummaryAsync(CompanyReportFilter filter, CancellationToken ct);

    /// <summary>The people of one company who reported the most tickets.</summary>
    Task<Outcome<List<CompanyClientStatDto>>> ClientsAsync(CompanyReportFilter filter, int limit, CancellationToken ct);

    Task<Outcome<List<BreakdownItemDto>>> TopicsAsync(CompanyReportFilter filter, CancellationToken ct);

    Task<Outcome<List<BreakdownItemDto>>> CategoriesAsync(CompanyReportFilter filter, CancellationToken ct);

    Task<Outcome<List<BranchCountDto>>> BranchesAsync(CompanyReportFilter filter, CancellationToken ct);

    Task<Outcome<ClosedByDto>> ClosedByAsync(CompanyReportFilter filter, int limit, CancellationToken ct);

    Task<Outcome<List<TimeSeriesPointDto>>> TimeSeriesAsync(CompanyReportFilter filter, CancellationToken ct);

    Task<Outcome<ExportFile>> ExportAsync(CompanyReportFilter filter, CancellationToken ct);
}

public sealed class CompanyReportService : ICompanyReportService
{
    /// <summary>"Top" is ten companies at most: a longer list is the page of all companies.</summary>
    public const int MostTop = 10;
    public const int MostPageSize = 100;
    public const int MostPeople = 100;
    private const int MostExportedCompanies = 1000;

    private static readonly string[] SortKeys = { "total", "name", "previous", "growth", "avgresolution", "sla", "open", "share" };

    private readonly AppDbContext _db;
    private readonly Lazy<TimeZoneInfo> _zone;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public CompanyReportService(AppDbContext db, IOptions<SlaOptions> slaOptions, TimeProvider time)
    {
        _db = db;
        _time = time;
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(slaOptions.Value.TimeZoneId));
    }

    // ---------------------------------------------------------------- the period and the groups of tickets

    private (DateTime From, DateTime To) Range(CompanyReportFilter filter)
    {
        var to = BusinessTimeZone.AsUtc(filter.To ?? UtcNow);
        var from = filter.From.HasValue ? BusinessTimeZone.AsUtc(filter.From.Value) : to.AddDays(-30);
        return (from, to);
    }

    /// <summary>The period of the same length right before this one; its end is the start of this one and is not part of it.</summary>
    private static (DateTime From, DateTime To) Previous(DateTime from, DateTime to) => (from - (to - from), from);

    /// <summary>The tickets of the company or the client that was asked for (every ticket when neither was), or only those of the given companies.</summary>
    private IQueryable<Ticket> Tickets(CompanyReportFilter filter, IReadOnlyCollection<Guid>? onlyCompanies = null)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();
        if (filter.CompanyId.HasValue) query = query.Where(t => t.CompanyId == filter.CompanyId.Value);
        else if (onlyCompanies is not null) query = query.Where(t => onlyCompanies.Contains(t.CompanyId));
        if (filter.ClientId.HasValue) query = query.Where(t => t.ClientId == filter.ClientId.Value);
        return query;
    }

    private static IQueryable<Ticket> CreatedIn(IQueryable<Ticket> tickets, DateTime from, DateTime to) =>
        tickets.Where(t => t.CreatedAt >= from && t.CreatedAt <= to);

    private static IQueryable<Ticket> ClosedIn(IQueryable<Ticket> tickets, DateTime from, DateTime to) =>
        tickets.Where(t => t.Status == TicketStatus.Closed && t.ClosedAtUtc != null && t.ClosedAtUtc >= from && t.ClosedAtUtc <= to);

    private async Task<IReadOnlyCollection<Guid>> TopCompanyIdsAsync(DateTime from, DateTime to, CancellationToken ct) =>
        await _db.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= from && t.CreatedAt <= to)
            .GroupBy(t => t.CompanyId)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Take(MostTop)
            .Select(g => g.Key)
            .ToListAsync(ct);

    /// <summary>What a company, a client or the lot is asked for must exist and make sense: a client needs a company, a company has to be there.</summary>
    private async Task<(bool Ok, bool NotFound, string? Message)> CheckAsync(CompanyReportFilter filter, bool companyRequired, CancellationToken ct)
    {
        if (filter.ClientId.HasValue && !filter.CompanyId.HasValue) return (false, false, "Choose a company first.");
        if (companyRequired && !filter.CompanyId.HasValue) return (false, false, "Choose a company first.");
        if (filter.CompanyId.HasValue && !await _db.Companies.AnyAsync(c => c.Id == filter.CompanyId.Value, ct)) return (false, true, "Company not found.");
        return (true, false, null);
    }

    private static Outcome<T> Refused<T>((bool Ok, bool NotFound, string? Message) check) =>
        check.NotFound ? Outcome<T>.NotFound(check.Message!) : Outcome<T>.Invalid(check.Message!);

    // ---------------------------------------------------------------- figures of a group of tickets

    private sealed class Figures
    {
        public Guid CompanyId { get; init; }
        public int Total { get; init; }
        public int New { get; init; }
        public int Open { get; init; }
        public int Closed { get; init; }
        public double? AvgMinutes { get; init; }
        public int WithDue { get; init; }
        public int OnTime { get; init; }
    }

    /// <summary>One row per company, counted by the database: the states, the resolution time of the closed ones, the SLA of those that have a due date.</summary>
    private static IQueryable<Figures> FiguresByCompany(IQueryable<Ticket> tickets) =>
        tickets.GroupBy(t => t.CompanyId).Select(g => new Figures
        {
            CompanyId = g.Key,
            Total = g.Count(),
            New = g.Count(t => t.Status == TicketStatus.New),
            // Resolved is not closed yet, so it is still work in progress.
            Open = g.Count(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress || t.Status == TicketStatus.Resolved),
            Closed = g.Count(t => t.Status == TicketStatus.Closed),
            AvgMinutes = g.Average(t => (double?)EF.Functions.DateDiffMinute(t.OpenedAtUtc, t.ClosedAtUtc)),
            WithDue = g.Count(t => t.ClosedAtUtc != null && t.DueDateUtc != null),
            OnTime = g.Count(t => t.ClosedAtUtc != null && t.DueDateUtc != null && t.ClosedAtUtc <= t.DueDateUtc),
        });

    private static double Hours(double? minutes) => minutes.HasValue ? Math.Round(minutes.Value / 60.0, 1) : 0;

    // No closed ticket has a due date: nothing to measure, which must not read as perfect compliance.
    private static double? Sla(int withDue, int onTime) => withDue > 0 ? Math.Round((double)onTime / withDue * 100, 1) : null;

    private static double Share(int part, int whole) => whole > 0 ? Math.Round((double)part / whole * 100, 1) : 0;

    private async Task<Dictionary<Guid, int>> CountsByCompanyAsync(DateTime from, DateTime toExclusive, IReadOnlyCollection<Guid>? onlyCompanies, CancellationToken ct)
    {
        var query = _db.Tickets.AsNoTracking().Where(t => t.CreatedAt >= from && t.CreatedAt < toExclusive);
        if (onlyCompanies is not null) query = query.Where(t => onlyCompanies.Contains(t.CompanyId));
        return await query.GroupBy(t => t.CompanyId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
    }

    private static CompanyRowDto Row(Figures f, string name, int previous, int totalTickets) =>
        new(f.CompanyId, name, f.Total, f.New, f.Open, f.Closed, Hours(f.AvgMinutes), Sla(f.WithDue, f.OnTime), previous, Share(f.Total, totalTickets));

    // ---------------------------------------------------------------- the rankings

    public async Task<TopCompaniesDto> TopCompaniesAsync(CompanyReportFilter filter, int limit, CancellationToken ct)
    {
        var (from, to) = Range(filter);
        var (prevFrom, prevTo) = Previous(from, to);
        var everyone = CreatedIn(_db.Tickets.AsNoTracking(), from, to);

        var totalTickets = await everyone.CountAsync(ct);
        var totalCompanies = await everyone.Select(t => t.CompanyId).Distinct().CountAsync(ct);
        var top = await FiguresByCompany(everyone).OrderByDescending(f => f.Total).ThenBy(f => f.CompanyId).Take(Math.Clamp(limit, 1, MostTop)).ToListAsync(ct);

        var ids = top.Select(f => f.CompanyId).ToList();
        var names = await _db.Companies.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var previous = await CountsByCompanyAsync(prevFrom, prevTo, ids, ct);

        var items = top.Select(f => Row(f, names.GetValueOrDefault(f.CompanyId, "Unknown"), previous.GetValueOrDefault(f.CompanyId), totalTickets)).ToList();
        return new TopCompaniesDto(items, totalTickets, items.Sum(i => i.Total), totalCompanies);
    }

    public async Task<CompanyListPageDto> ListCompaniesAsync(CompanyReportFilter filter, string? search, string? sort, bool descending, int page, int pageSize, CancellationToken ct)
    {
        var (from, to) = Range(filter);
        var (prevFrom, prevTo) = Previous(from, to);
        pageSize = Math.Clamp(pageSize, 1, MostPageSize);
        page = Math.Max(page, 1);

        var everyone = CreatedIn(_db.Tickets.AsNoTracking(), from, to);
        var totalTickets = await everyone.CountAsync(ct);
        var figures = await FiguresByCompany(everyone).ToDictionaryAsync(f => f.CompanyId, ct);
        var previous = await CountsByCompanyAsync(prevFrom, prevTo, null, ct);

        // Every company is on the list, the ones with nothing in the period too (nothing is also an answer), and a switched-off one that has tickets in it.
        var withTickets = figures.Keys.Concat(previous.Keys).Distinct().ToList();
        var companies = await _db.Companies.AsNoTracking()
            .Where(c => c.IsActive || withTickets.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        var rows = companies.Select(c =>
        {
            var f = figures.GetValueOrDefault(c.Id) ?? new Figures { CompanyId = c.Id };
            return Row(f, c.Name, previous.GetValueOrDefault(c.Id), totalTickets);
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            rows = rows.Where(r => r.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        var key = SortKeys.Contains((sort ?? "").ToLowerInvariant()) ? sort!.ToLowerInvariant() : "total";
        Func<CompanyRowDto, object?> by = key switch
        {
            "name" => r => r.Name.ToLowerInvariant(),
            "previous" => r => r.PreviousTotal,
            "growth" => r => r.Total - r.PreviousTotal,
            "avgresolution" => r => r.AvgResolutionHours,
            "sla" => r => r.SlaComplianceRate ?? -1,
            "open" => r => r.Open,
            "share" => r => r.SharePercent,
            _ => r => r.Total,
        };
        // The direction asked for; names alphabetically when equal, so that the pages do not shuffle between two requests.
        var ordered = (descending ? rows.OrderByDescending(by) : rows.OrderBy(by)).ThenBy(r => r.Name.ToLowerInvariant()).ThenBy(r => r.CompanyId).ToList();

        return new CompanyListPageDto(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, ordered.Count, totalTickets);
    }

    // ---------------------------------------------------------------- one company, or one person of it

    public async Task<Outcome<CompanySummaryDto>> SummaryAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: true, ct);
        if (!check.Ok) return Refused<CompanySummaryDto>(check);

        var (from, to) = Range(filter);
        var (prevFrom, prevTo) = Previous(from, to);
        var figures = await FiguresByCompany(CreatedIn(Tickets(filter), from, to)).FirstOrDefaultAsync(ct) ?? new Figures();
        var previous = await Tickets(filter).CountAsync(t => t.CreatedAt >= prevFrom && t.CreatedAt < prevTo, ct);

        return Outcome<CompanySummaryDto>.Ok(new CompanySummaryDto(figures.Total, figures.New, figures.Open, figures.Closed, Hours(figures.AvgMinutes), Sla(figures.WithDue, figures.OnTime), previous));
    }

    public async Task<Outcome<List<CompanyClientStatDto>>> ClientsAsync(CompanyReportFilter filter, int limit, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: true, ct);
        if (!check.Ok) return Refused<List<CompanyClientStatDto>>(check);

        var (from, to) = Range(filter);
        var grouped = await CreatedIn(Tickets(filter), from, to)
            .GroupBy(t => t.ClientId)
            .Select(g => new
            {
                ClientId = g.Key,
                Total = g.Count(),
                Open = g.Count(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress || t.Status == TicketStatus.Resolved),
                Closed = g.Count(t => t.Status == TicketStatus.Closed),
            })
            .OrderByDescending(x => x.Total).ThenBy(x => x.ClientId)
            .Take(Math.Clamp(limit, 1, MostPeople))
            .ToListAsync(ct);

        var ids = grouped.Select(x => x.ClientId).ToList();
        var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        return Outcome<List<CompanyClientStatDto>>.Ok(grouped.Select(x => new CompanyClientStatDto(x.ClientId, names.GetValueOrDefault(x.ClientId, "Unknown"), x.Total, x.Open, x.Closed)).ToList());
    }

    // ---------------------------------------------------------------- topics and categories: two lists, by id, whatever has been added since

    /// <summary>The tickets that are asked about: those of the company or client, or, when neither was chosen, those of the ten companies with the most tickets in the period.</summary>
    private async Task<IQueryable<Ticket>> ScopeAsync(CompanyReportFilter filter, DateTime from, DateTime to, CancellationToken ct)
    {
        IReadOnlyCollection<Guid>? top = filter.CompanyId.HasValue ? null : await TopCompanyIdsAsync(from, to, ct);
        return Tickets(filter, top);
    }

    public async Task<Outcome<List<BreakdownItemDto>>> TopicsAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<List<BreakdownItemDto>>(check);

        var (from, to) = Range(filter);
        var grouped = await CreatedIn(await ScopeAsync(filter, from, to, ct), from, to)
            .GroupBy(t => t.HelpTopicId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);

        var ids = grouped.Where(g => g.Id.HasValue).Select(g => g.Id!.Value).ToList();
        // Names are looked up for the switched-off ones too: a topic that is not offered any more still has the tickets it had.
        var names = await _db.HelpTopics.AsNoTracking().Where(h => ids.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => new { h.Name, h.IsActive }, ct);
        return Outcome<List<BreakdownItemDto>>.Ok(Items(grouped.Select(g => (g.Id, g.Count)), names.ToDictionary(x => x.Key, x => (x.Value.Name, x.Value.IsActive))));
    }

    public async Task<Outcome<List<BreakdownItemDto>>> CategoriesAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<List<BreakdownItemDto>>(check);

        var (from, to) = Range(filter);
        var grouped = await CreatedIn(await ScopeAsync(filter, from, to, ct), from, to)
            .GroupBy(t => t.CategoryId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);

        var ids = grouped.Where(g => g.Id.HasValue).Select(g => g.Id!.Value).ToList();
        var names = await _db.TicketCategories.AsNoTracking().Where(h => ids.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => new { h.Name, h.IsActive }, ct);
        return Outcome<List<BreakdownItemDto>>.Ok(Items(grouped.Select(g => (g.Id, g.Count)), names.ToDictionary(x => x.Key, x => (x.Value.Name, x.Value.IsActive))));
    }

    /// <summary>Most tickets first; "none" (a ticket that has not been given one) is a row of its own, so that the rows add up to the total, and it is last among equals.</summary>
    private static List<BreakdownItemDto> Items(IEnumerable<(Guid? Id, int Count)> grouped, Dictionary<Guid, (string Name, bool IsActive)> names) =>
        grouped
            .Select(g => g.Id.HasValue
                ? new BreakdownItemDto(g.Id, names.TryGetValue(g.Id.Value, out var n) ? n.Name : "Unknown", g.Count, !names.TryGetValue(g.Id.Value, out var m) || m.IsActive)
                : new BreakdownItemDto(null, null, g.Count, true))
            .OrderByDescending(i => i.Count).ThenBy(i => i.Id is null).ThenBy(i => i.Name)
            .ToList();

    // ---------------------------------------------------------------- branches

    public async Task<Outcome<List<BranchCountDto>>> BranchesAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<List<BranchCountDto>>(check);

        var (from, to) = Range(filter);
        var grouped = await CreatedIn(await ScopeAsync(filter, from, to, ct), from, to)
            .GroupBy(t => new { t.DepartmentId, t.SubBranchId }).Select(g => new { g.Key.DepartmentId, g.Key.SubBranchId, Count = g.Count() }).ToListAsync(ct);
        return Outcome<List<BranchCountDto>>.Ok(await BranchCountsAsync(grouped.Select(g => (g.DepartmentId, g.SubBranchId, g.Count)), ct));
    }

    private async Task<List<BranchCountDto>> BranchCountsAsync(IEnumerable<(Guid? DepartmentId, Guid? SubBranchId, int Count)> rows, CancellationToken ct)
    {
        var list = rows.ToList();
        var departments = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var subs = await _db.SubBranches.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return list
            .GroupBy(r => r.DepartmentId)
            .Select(g => new BranchCountDto(
                g.Key,
                g.Key.HasValue ? departments.GetValueOrDefault(g.Key.Value, "Unknown") : null,
                g.Sum(r => r.Count),
                g.Where(r => r.SubBranchId is null).Sum(r => r.Count),
                g.Where(r => r.SubBranchId.HasValue)
                    .GroupBy(r => r.SubBranchId!.Value)
                    .Select(s => new SubBranchCountDto(s.Key, subs.GetValueOrDefault(s.Key, "Unknown"), s.Sum(r => r.Count)))
                    .OrderByDescending(s => s.Count).ThenBy(s => s.Name).ToList()))
            .OrderByDescending(b => b.Count).ThenBy(b => b.DepartmentId is null).ThenBy(b => b.Name)
            .ToList();
    }

    // ---------------------------------------------------------------- who solves it

    public async Task<Outcome<ClosedByDto>> ClosedByAsync(CompanyReportFilter filter, int limit, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<ClosedByDto>(check);

        var (from, to) = Range(filter);
        // The tickets closed in the period, whenever they were created: the person who closed a ticket is the one who worked with the company.
        var closed = ClosedIn(await ScopeAsync(filter, from, to, ct), from, to);

        var perPerson = await closed.Where(t => t.ClosedByUserId != null)
            .GroupBy(t => new { UserId = t.ClosedByUserId!.Value, t.DepartmentId }).Select(g => new { g.Key.UserId, g.Key.DepartmentId, Count = g.Count() }).ToListAsync(ct);
        var perBranch = await closed.GroupBy(t => new { t.DepartmentId, t.SubBranchId }).Select(g => new { g.Key.DepartmentId, g.Key.SubBranchId, Count = g.Count() }).ToListAsync(ct);

        var people = perPerson.GroupBy(r => r.UserId).Select(g => new { UserId = g.Key, Total = g.Sum(r => r.Count), Rows = g.ToList() })
            .OrderByDescending(p => p.Total).ThenBy(p => p.UserId).Take(Math.Clamp(limit, 1, MostPeople)).ToList();

        var userIds = people.Select(p => p.UserId).ToList();
        var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var departments = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var result = people.Select(p => new CloserStatDto(
            p.UserId,
            names.GetValueOrDefault(p.UserId, "Unknown"),
            p.Total,
            p.Rows.OrderByDescending(r => r.Count)
                .Select(r => new CloserBranchDto(r.DepartmentId, r.DepartmentId.HasValue ? departments.GetValueOrDefault(r.DepartmentId.Value, "Unknown") : null, r.Count)).ToList())).ToList();

        var branches = await BranchCountsAsync(perBranch.Select(r => (r.DepartmentId, r.SubBranchId, r.Count)), ct);
        return Outcome<ClosedByDto>.Ok(new ClosedByDto(result, branches, perBranch.Sum(r => r.Count)));
    }

    // ---------------------------------------------------------------- over time

    public async Task<Outcome<List<TimeSeriesPointDto>>> TimeSeriesAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<List<TimeSeriesPointDto>>(check);

        var (from, to) = Range(filter);
        var zone = _zone.Value;
        var scope = await ScopeAsync(filter, from, to, ct);

        // Two groups: what was opened on a day, and what was closed on it; days are the calendar days of the business, not UTC days.
        var created = await CreatedIn(scope, from, to).Select(t => t.CreatedAt).ToListAsync(ct);
        var closed = await ClosedIn(scope, from, to).Select(t => t.ClosedAtUtc!.Value).ToListAsync(ct);
        var opened = created.GroupBy(d => BusinessTimeZone.LocalDate(d, zone)).ToDictionary(g => g.Key, g => g.Count());
        var done = closed.GroupBy(d => BusinessTimeZone.LocalDate(d, zone)).ToDictionary(g => g.Key, g => g.Count());

        var points = new List<TimeSeriesPointDto>();
        for (var d = BusinessTimeZone.LocalDate(from, zone); d <= BusinessTimeZone.LocalDate(to, zone); d = d.AddDays(1))
            points.Add(new TimeSeriesPointDto(d, opened.GetValueOrDefault(d), done.GetValueOrDefault(d)));
        return Outcome<List<TimeSeriesPointDto>>.Ok(points);
    }

    // ---------------------------------------------------------------- the workbook

    public async Task<Outcome<ExportFile>> ExportAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<ExportFile>(check);

        var (from, to) = Range(filter);
        string scope;
        var companies = new List<CompanyExportRow>();
        if (filter.CompanyId.HasValue)
        {
            var companyName = await _db.Companies.AsNoTracking().Where(c => c.Id == filter.CompanyId.Value).Select(c => c.Name).FirstAsync(ct);
            var clientName = filter.ClientId.HasValue
                ? await _db.Users.AsNoTracking().Where(u => u.Id == filter.ClientId.Value).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct) ?? "Unknown"
                : null;
            scope = clientName is null ? $"Company: {companyName}" : $"Company: {companyName}; client: {clientName}";
            var summary = (await SummaryAsync(filter, ct)).Value!;
            companies.Add(new CompanyExportRow(companyName, summary.Total, summary.New, summary.Open, summary.Closed, summary.AvgResolutionHours, summary.SlaComplianceRate, summary.PreviousTotal, 100));
        }
        else
        {
            scope = "All companies (the distributions below are for the ten with the most tickets)";
            var page = await ListCompaniesAsync(filter, null, "total", true, 1, MostPageSize, ct);
            // The whole list, a hundred rows at a time (a page has an upper limit like everywhere else), up to a most.
            var rows = new List<CompanyRowDto>(page.Items);
            for (var p = 2; rows.Count < Math.Min(page.TotalCount, MostExportedCompanies); p++)
                rows.AddRange((await ListCompaniesAsync(filter, null, "total", true, p, MostPageSize, ct)).Items);
            companies = rows.Take(MostExportedCompanies).Select(r => new CompanyExportRow(r.Name, r.Total, r.New, r.Open, r.Closed, r.AvgResolutionHours, r.SlaComplianceRate, r.PreviousTotal, r.SharePercent)).ToList();
        }

        var clients = filter.CompanyId.HasValue && !filter.ClientId.HasValue
            ? (await ClientsAsync(filter, MostPeople, ct)).Value!.Select(c => new ClientExportRow(c.Name, c.Total, c.Open, c.Closed)).ToList()
            : new List<ClientExportRow>();
        var topics = (await TopicsAsync(filter, ct)).Value!.Select(i => new NamedCountExportRow(i.Name ?? "(none)", i.Count)).ToList();
        var categories = (await CategoriesAsync(filter, ct)).Value!.Select(i => new NamedCountExportRow(i.Name ?? "(none)", i.Count)).ToList();
        var branches = (await BranchesAsync(filter, ct)).Value!.Select(b => new NamedCountExportRow(b.Name ?? "(none)", b.Count)).ToList();
        var closers = (await ClosedByAsync(filter, MostPeople, ct)).Value!.People.Select(p => new CloserExportRow(p.Name, p.ClosedCount)).ToList();

        var bytes = CompanyReportExcelExporter.Build(new CompanyReportExport(scope, from, to, companies, clients, topics, categories, branches, closers));
        return Outcome<ExportFile>.Ok(new ExportFile(bytes, $"company-statistics-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx"));
    }
}
