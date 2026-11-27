using System.Text;
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
/// The reports about people: what is on their plate, the hours they recorded, what they are good at, and who is the only one who knows a company. Read only.
/// They are about a PERSON, so the rule is not the one of branches: Admin sees everybody (and may narrow to one branch), an employee sees only themself,
/// and asking for another person is refused (403). The dependency of companies on one person is for Admin only.
/// </summary>
public interface IEmployeeReportService
{
    Task<WorkloadDto> WorkloadAsync(Guid? departmentId, CancellationToken ct);

    Task<HoursPeopleDto> HoursAsync(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct);

    Task<Outcome<HoursDetailDto>> HoursOfAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct);

    /// <summary>Top three topics, categories and companies of a person, by the tickets they closed in the period and the hours they recorded. Admin must say whom; an employee gets themself.</summary>
    Task<Outcome<SpecializationDto>> SpecializationAsync(Guid? userId, DateTime? from, DateTime? to, CancellationToken ct);

    /// <summary>Companies with at least <paramref name="minClosed"/> tickets closed in the last 12 months where one person closed <paramref name="minSharePercent"/> or more of them.</summary>
    Task<DependencyDto> DependencyAsync(int minClosed, double minSharePercent, CancellationToken ct);

    /// <summary>The monthly worksheet of a person as an xlsx workbook, in the language asked for or, without one, the language of the person who asks.</summary>
    Task<Outcome<ExportFile>> TimesheetAsync(Guid userId, int year, int month, string? language, CancellationToken ct);
}

public sealed class EmployeeReportService : IEmployeeReportService
{
    public const int DefaultMinClosed = 5;
    public const double DefaultMinSharePercent = 50;
    public const double HighSharePercent = 80;
    public const int MostDependencies = 100;
    public const int TopOfAKind = 3;

    private static readonly TicketStatus[] Active = { TicketStatus.New, TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved };

    private sealed record Person(Guid Id, string Name, UserRole Role, bool IsActive, Guid? DepartmentId, DateTime CreatedAtUtc, string Language);

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly Lazy<TimeZoneInfo> _zone;

    public EmployeeReportService(AppDbContext db, ICurrentUserService currentUser, IOptions<SlaOptions> sla, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _time = time;
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(sla.Value.TimeZoneId));
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;
    private bool IsAdmin => _currentUser.Role == UserRole.Admin;

    private (DateTime From, DateTime To) Period(DateTime? from, DateTime? to)
    {
        var t = BusinessTimeZone.AsUtc(to ?? UtcNow);
        return (from.HasValue ? BusinessTimeZone.AsUtc(from.Value) : t.AddDays(-30), t);
    }

    private IQueryable<Infrastructure.Identity.ApplicationUser> UserRows => _db.Users.AsNoTracking();

    private static readonly System.Linq.Expressions.Expression<Func<Infrastructure.Identity.ApplicationUser, Person>> ToPerson =
        u => new Person(u.Id, u.FirstName + " " + u.LastName, u.Role, u.IsActive, u.DepartmentId, u.CreatedAtUtc, u.PreferredLanguage);

    private async Task<Dictionary<Guid, string>> BranchNamesAsync(CancellationToken ct) => await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

    /// <summary>The people a report lists: for an employee, themself; for Admin, the active employees (of one branch if asked) and anybody else named in <paramref name="alsoThese"/>.</summary>
    private async Task<List<Person>> ListedAsync(Guid? departmentId, IReadOnlyCollection<Guid> alsoThese, CancellationToken ct)
    {
        if (!IsAdmin)
        {
            var me = _currentUser.UserId;
            return await UserRows.Where(u => u.Id == me).Select(ToPerson).ToListAsync(ct);
        }
        var people = await UserRows
            .Where(u => (u.Role == UserRole.Employee && u.IsActive) || alsoThese.Contains(u.Id))
            .Where(u => departmentId == null || u.DepartmentId == departmentId)
            .Select(ToPerson)
            .ToListAsync(ct);
        return people.OrderBy(p => p.Name).ToList();
    }

    // ---------------------------------------------------------------- the load

    public async Task<WorkloadDto> WorkloadAsync(Guid? departmentId, CancellationToken ct)
    {
        var now = UtcNow;
        var me = IsAdmin ? (Guid?)null : _currentUser.UserId;

        var tickets = await _db.TicketAssignees.AsNoTracking()
            .Where(a => Active.Contains(a.Ticket!.Status) && (me == null || a.UserId == me))
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Open = g.Count(), Main = g.Count(a => a.IsPrimary), Overdue = g.Count(a => a.Ticket!.DueDateUtc != null && a.Ticket.DueDateUtc < now) })
            .ToDictionaryAsync(x => x.UserId, ct);
        var tasks = await _db.WorkTasks.AsNoTracking()
            .Where(t => t.Status != WorkTaskStatus.Done && (me == null || t.AssignedToUserId == me))
            .GroupBy(t => t.AssignedToUserId)
            .Select(g => new { UserId = g.Key, Open = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Open, ct);

        var withLoad = tickets.Keys.Concat(tasks.Keys).ToList();
        var people = await ListedAsync(departmentId, withLoad.Where(_ => true).ToList(), ct);
        var branches = await BranchNamesAsync(ct);

        var rows = people
            .Where(p => IsAdmin ? (p.Role == UserRole.Employee || tickets.ContainsKey(p.Id) || tasks.ContainsKey(p.Id)) : true)
            .Select(p =>
            {
                var t = tickets.GetValueOrDefault(p.Id);
                return new PersonLoadDto(p.Id, p.Name, p.DepartmentId is { } d ? branches.GetValueOrDefault(d) : null, t?.Open ?? 0, t?.Main ?? 0, t?.Overdue ?? 0, tasks.GetValueOrDefault(p.Id));
            })
            .OrderByDescending(r => r.OpenTickets + r.OpenTasks)
            .ThenBy(r => r.Name)
            .ToList();
        return new WorkloadDto(now, rows);
    }

    // ---------------------------------------------------------------- the hours

    /// <summary>The recorded sessions (tickets and tasks) of some people that STARTED in the range. Labels (numbers, titles, companies) only when they are used.</summary>
    private async Task<List<WorkEntry>> EntriesAsync(IReadOnlyCollection<Guid> userIds, DateTime from, DateTime to, bool labels, CancellationToken ct)
    {
        var ticketRows = await _db.TicketWorkSessions.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId) && s.StartedAtUtc >= from && s.StartedAtUtc <= to)
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.TicketId,
                s.StartedAtUtc,
                s.EndedAtUtc,
                Number = labels ? s.Ticket!.TicketNumber : "",
                Title = labels ? s.Ticket!.Title : "",
                CompanyId = (Guid?)s.Ticket!.CompanyId,
                Company = labels ? s.Ticket!.Company!.Name : null
            })
            .ToListAsync(ct);
        var taskRows = await _db.WorkTaskSessions.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId) && s.StartedAtUtc >= from && s.StartedAtUtc <= to)
            .Select(s => new { s.Id, s.UserId, s.WorkTaskId, s.StartedAtUtc, s.EndedAtUtc, Title = labels ? s.WorkTask!.Title : "" })
            .ToListAsync(ct);

        return ticketRows.Select(r => new WorkEntry(r.Id, r.UserId, false, r.TicketId, r.Number, r.Title, r.CompanyId, r.Company, r.StartedAtUtc, r.EndedAtUtc))
            .Concat(taskRows.Select(r => new WorkEntry(r.Id, r.UserId, true, r.WorkTaskId, "", r.Title, null, null, r.StartedAtUtc, r.EndedAtUtc)))
            .ToList();
    }

    private static bool CountsDaysWithout(Person p) => p.Role == UserRole.Employee && p.IsActive;

    public async Task<HoursPeopleDto> HoursAsync(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        var now = UtcNow;
        var zone = _zone.Value;
        var people = await ListedAsync(departmentId, Array.Empty<Guid>(), ct);
        var entries = (await EntriesAsync(people.Select(p => p.Id).ToList(), f, t, false, ct)).ToLookup(e => e.UserId);
        var branches = await BranchNamesAsync(ct);
        var first = BusinessTimeZone.LocalDate(f, zone);
        var last = BusinessTimeZone.LocalDate(t, zone);
        var today = BusinessTimeZone.LocalDate(now, zone);

        var rows = people.Select(p =>
        {
            var mine = entries[p.Id].ToList();
            var days = WorkTimeSummary.ByDay(mine, first, last, zone, now);
            var ticket = days.Sum(d => d.TicketSeconds);
            var task = days.Sum(d => d.TaskSeconds);
            int? without = CountsDaysWithout(p) ? WorkTimeSummary.DaysWithoutWork(days, BusinessTimeZone.LocalDate(p.CreatedAtUtc, zone), today).Count : null;
            return new PersonHoursDto(p.Id, p.Name, p.DepartmentId is { } d ? branches.GetValueOrDefault(d) : null,
                WorkTimeSummary.Hours(ticket), WorkTimeSummary.Hours(task), WorkTimeSummary.Hours(ticket + task), without, WorkTimeSummary.RunningCount(mine));
        }).OrderByDescending(r => r.TotalHours).ThenBy(r => r.Name).ToList();
        return new HoursPeopleDto(rows);
    }

    /// <summary>The person asked for, if the person asking may see them: Admin anybody (not found if there is no such person), an employee only themself.</summary>
    private async Task<(Person? Person, Outcome<T>? Refusal)> PersonAsync<T>(Guid userId, CancellationToken ct)
    {
        if (!IsAdmin && userId != _currentUser.UserId) return (null, Outcome<T>.Forbidden());
        var person = await UserRows.Where(u => u.Id == userId).Select(ToPerson).FirstOrDefaultAsync(ct);
        return person is null ? (null, Outcome<T>.NotFound()) : (person, null);
    }

    public async Task<Outcome<HoursDetailDto>> HoursOfAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (person, refusal) = await PersonAsync<HoursDetailDto>(userId, ct);
        if (person is null) return refusal!;

        var (f, t) = Period(from, to);
        var now = UtcNow;
        var zone = _zone.Value;
        var entries = await EntriesAsync(new[] { userId }, f, t, false, ct);
        var days = WorkTimeSummary.ByDay(entries, BusinessTimeZone.LocalDate(f, zone), BusinessTimeZone.LocalDate(t, zone), zone, now);
        var ticket = days.Sum(d => d.TicketSeconds);
        var task = days.Sum(d => d.TaskSeconds);
        var applies = CountsDaysWithout(person);
        var without = applies ? WorkTimeSummary.DaysWithoutWork(days, BusinessTimeZone.LocalDate(person.CreatedAtUtc, zone), BusinessTimeZone.LocalDate(now, zone)) : new List<DateOnly>();

        return Outcome<HoursDetailDto>.Ok(new HoursDetailDto(
            person.Id, person.Name, WorkTimeSummary.Hours(ticket), WorkTimeSummary.Hours(task), WorkTimeSummary.Hours(ticket + task),
            days.Select(d => new DayHoursDto(d.Day, WorkTimeSummary.Hours(d.TicketSeconds), WorkTimeSummary.Hours(d.TaskSeconds), WorkTimeSummary.Hours(d.TotalSeconds))).ToList(),
            WorkTimeSummary.ByWeek(days).Select(w => new WeekHoursDto(w.WeekStart, WorkTimeSummary.Hours(w.TicketSeconds), WorkTimeSummary.Hours(w.TaskSeconds), WorkTimeSummary.Hours(w.TotalSeconds))).ToList(),
            without, applies, WorkTimeSummary.RunningCount(entries)));
    }

    // ---------------------------------------------------------------- what a person is good at

    public async Task<Outcome<SpecializationDto>> SpecializationAsync(Guid? userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var id = userId ?? (IsAdmin ? null : _currentUser.UserId); // an employee who names nobody gets themself; naming another is refused below
        if (id is null) return Outcome<SpecializationDto>.Invalid("Choose a person first.");
        var (person, refusal) = await PersonAsync<SpecializationDto>(id.Value, ct);
        if (person is null) return refusal!;

        var (f, t) = Period(from, to);
        var now = UtcNow;

        // What they closed, by topic, category and company.
        var closed = await _db.Tickets.AsNoTracking()
            .Where(x => x.ClosedByUserId == id && x.Status == TicketStatus.Closed && x.ClosedAtUtc != null && x.ClosedAtUtc >= f && x.ClosedAtUtc <= t)
            .Select(x => new { x.HelpTopicId, x.CategoryId, x.CompanyId })
            .ToListAsync(ct);

        // The hours they recorded on tickets, by the same.
        var sessions = await _db.TicketWorkSessions.AsNoTracking()
            .Where(s => s.UserId == id && s.StartedAtUtc >= f && s.StartedAtUtc <= t)
            .Select(s => new { s.TicketId, s.StartedAtUtc, s.EndedAtUtc, s.Ticket!.HelpTopicId, s.Ticket.CategoryId, s.Ticket.CompanyId })
            .ToListAsync(ct);
        long Seconds(DateTime start, DateTime? end) => (end ?? now) > start ? (long)Math.Floor(((end ?? now) - start).TotalSeconds) : 0;

        List<(Guid Id, int Closed, long Seconds)> Combine(IEnumerable<Guid?> closedKeys, IEnumerable<(Guid? Key, long Seconds)> worked)
        {
            var closedBy = closedKeys.Where(k => k.HasValue).GroupBy(k => k!.Value).ToDictionary(g => g.Key, g => g.Count());
            var secondsBy = worked.Where(w => w.Key.HasValue).GroupBy(w => w.Key!.Value).ToDictionary(g => g.Key, g => g.Sum(w => w.Seconds));
            return closedBy.Keys.Union(secondsBy.Keys).Select(k => (k, closedBy.GetValueOrDefault(k), secondsBy.GetValueOrDefault(k)))
                .OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item3).Take(TopOfAKind).ToList();
        }

        var topics = Combine(closed.Select(c => c.HelpTopicId), sessions.Select(s => (s.HelpTopicId, Seconds(s.StartedAtUtc, s.EndedAtUtc))));
        var categories = Combine(closed.Select(c => c.CategoryId), sessions.Select(s => (s.CategoryId, Seconds(s.StartedAtUtc, s.EndedAtUtc))));
        var companies = Combine(closed.Select(c => (Guid?)c.CompanyId), sessions.Select(s => ((Guid?)s.CompanyId, Seconds(s.StartedAtUtc, s.EndedAtUtc))));

        var topicIds = topics.Select(x => x.Id).ToList();
        var categoryIds = categories.Select(x => x.Id).ToList();
        var companyIds = companies.Select(x => x.Id).ToList();
        var topicNames = await _db.HelpTopics.AsNoTracking().Where(x => topicIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var categoryNames = await _db.TicketCategories.AsNoTracking().Where(x => categoryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var companyNames = await _db.Companies.AsNoTracking().Where(x => companyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        List<SpecializationItemDto> Items(List<(Guid Id, int Closed, long Seconds)> top, Dictionary<Guid, string> names) =>
            top.Select(x => new SpecializationItemDto(x.Id, names.GetValueOrDefault(x.Id, "?"), x.Closed, WorkTimeSummary.Hours(x.Seconds))).ToList();

        return Outcome<SpecializationDto>.Ok(new SpecializationDto(person.Id, person.Name, closed.Count, Items(topics, topicNames), Items(categories, categoryNames), Items(companies, companyNames)));
    }

    // ---------------------------------------------------------------- dependence on one person

    public async Task<DependencyDto> DependencyAsync(int minClosed, double minSharePercent, CancellationToken ct)
    {
        minClosed = Math.Clamp(minClosed, 1, 1000);
        minSharePercent = Math.Clamp(minSharePercent, 1, 100);
        var since = UtcNow.AddMonths(-12);

        var rows = await _db.Tickets.AsNoTracking()
            .Where(x => x.Status == TicketStatus.Closed && x.ClosedAtUtc != null && x.ClosedAtUtc >= since)
            .GroupBy(x => new { x.CompanyId, x.ClosedByUserId })
            .Select(g => new { g.Key.CompanyId, g.Key.ClosedByUserId, Count = g.Count() })
            .ToListAsync(ct);

        var found = rows.GroupBy(r => r.CompanyId)
            .Select(g =>
            {
                var total = g.Sum(r => r.Count);
                var top = g.Where(r => r.ClosedByUserId != null).OrderByDescending(r => r.Count).FirstOrDefault();
                return (CompanyId: g.Key, Total: total, Top: top);
            })
            .Where(x => x.Total >= minClosed && x.Top is not null && x.Top.Count * 100.0 / x.Total >= minSharePercent)
            .OrderByDescending(x => x.Top!.Count * 1.0 / x.Total).ThenByDescending(x => x.Total)
            .ToList();

        var shown = found.Take(MostDependencies).ToList();
        var companyIds = shown.Select(x => x.CompanyId).ToList();
        var personIds = shown.Select(x => x.Top!.ClosedByUserId!.Value).Distinct().ToList();
        var companies = await _db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var people = (await UserRows.Where(u => personIds.Contains(u.Id)).Select(ToPerson).ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Name);

        var items = shown.Select(x =>
        {
            var share = Math.Round(x.Top!.Count * 100.0 / x.Total, 1);
            var person = x.Top.ClosedByUserId!.Value;
            return new CompanyDependencyDto(x.CompanyId, companies.GetValueOrDefault(x.CompanyId, "?"), x.Total, person, people.GetValueOrDefault(person, "?"), x.Top.Count, share, share >= HighSharePercent, x.Top.Count == x.Total);
        }).ToList();
        return new DependencyDto(found.Count, minClosed, minSharePercent, items);
    }

    // ---------------------------------------------------------------- the worksheet

    public async Task<Outcome<ExportFile>> TimesheetAsync(Guid userId, int year, int month, string? language, CancellationToken ct)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100) return Outcome<ExportFile>.Invalid("The month is not valid.");
        var (person, refusal) = await PersonAsync<ExportFile>(userId, ct);
        if (person is null) return refusal!;

        var now = UtcNow;
        var zone = _zone.Value;
        var makerId = _currentUser.UserId;
        var maker = await UserRows.Where(u => u.Id == makerId).Select(ToPerson).FirstAsync(ct);
        var text = ReportText.For(language is null ? maker.Language : EmailLanguage.Normalize(language));

        var (start, end) = BusinessMonth.Window(year, month, zone);
        var entries = (await EntriesAsync(new[] { userId }, start, end.AddTicks(-1), true, ct)).OrderBy(e => e.StartUtc).ToList();
        var days = WorkTimeSummary.ByDay(entries, new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)), zone, now);

        var document = new TimesheetDocument(person.Name, year, month, maker.Name, now, zone, days,
            WorkTimeSummary.ByCompany(entries, text("ts_tasksNoCompany"), now), WorkTimeSummary.ByItem(entries, now), entries, text);
        var content = TimesheetExcelExporter.Build(document);
        return Outcome<ExportFile>.Ok(new ExportFile(content, $"timesheet-{AsciiName(person.Name)}-{year}-{month:00}.xlsx"));
    }

    private static string AsciiName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '-') sb.Append('-');
        }
        var result = sb.ToString().Trim('-');
        return result.Length == 0 ? "person" : result;
    }
}
