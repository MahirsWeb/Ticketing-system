using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Reports;

// The card of a company and the load of the branches that work for it. Same rules as the rest of the company statistics: totals and names only, all branches together,
// whoever asks. Nothing new in the database: everything is read from the tickets, the work sessions and the accounts.
public sealed partial class CompanyReportService
{
    public const string NoBranchLabel = "";

    // ---------------------------------------------------------------- the card

    public async Task<Outcome<CompanyCardDto>> CardAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: true, ct);
        if (!check.Ok) return Refused<CompanyCardDto>(check);

        var companyId = filter.CompanyId!.Value;
        var (from, to) = Range(filter);
        var now = UtcNow;
        var zone = _zone.Value;
        var tickets = _db.Tickets.AsNoTracking().Where(t => t.CompanyId == companyId);

        var summary = await tickets.GroupBy(_ => 1).Select(g => new { Total = g.Count(), First = g.Min(t => t.CreatedAt), Last = g.Max(t => t.CreatedAt) }).FirstOrDefaultAsync(ct);

        // By year: the local year of the business, so a ticket of 00:30 on 1 January is of the new year although it is still December in UTC.
        var created = await tickets.Select(t => t.CreatedAt).ToListAsync(ct);
        var byYear = created.GroupBy(c => BusinessTimeZone.LocalDate(c, zone).Year).OrderBy(g => g.Key).Select(g => new YearCountDto(g.Key, g.Count())).ToList();

        // The clients: the active accounts of the company, and which of them wrote.
        var registered = await _db.Users.AsNoTracking().Where(u => u.Role == UserRole.Client && u.CompanyId == companyId && u.IsActive).Select(u => u.Id).ToListAsync(ct);
        var inPeriod = await CreatedIn(tickets, from, to).Where(t => registered.Contains(t.ClientId)).Select(t => t.ClientId).Distinct().CountAsync(ct);
        var ever = await tickets.Where(t => registered.Contains(t.ClientId)).Select(t => t.ClientId).Distinct().CountAsync(ct);

        // All the work ever recorded on its tickets: a finished session by its length, a running one up to now.
        var sessions = _db.TicketWorkSessions.AsNoTracking().Where(s => s.Ticket!.CompanyId == companyId);
        var finished = await sessions.Where(s => s.EndedAtUtc != null).SumAsync(s => (long?)EF.Functions.DateDiffSecond(s.StartedAtUtc, s.EndedAtUtc!.Value), ct) ?? 0;
        var running = await sessions.Where(s => s.EndedAtUtc == null && s.StartedAtUtc < now).SumAsync(s => (long?)EF.Functions.DateDiffSecond(s.StartedAtUtc, now), ct) ?? 0;

        return Outcome<CompanyCardDto>.Ok(new CompanyCardDto(
            summary?.First, summary?.Last, summary?.Total ?? 0, byYear, registered.Count, inPeriod, ever, Math.Round((finished + running) / 3600.0, 1)));
    }

    // ---------------------------------------------------------------- the branches that work for the company

    private sealed record Place(Guid Branch, Guid Sub); // Guid.Empty stands for none: a dictionary does not take a null key

    public async Task<Outcome<BranchLoadDto>> BranchLoadAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: true, ct);
        if (!check.Ok) return Refused<BranchLoadDto>(check);

        var companyId = filter.CompanyId!.Value;
        var (from, to) = Range(filter);
        var now = UtcNow;
        var tickets = _db.Tickets.AsNoTracking().Where(t => t.CompanyId == companyId);

        var created = await CreatedIn(tickets, from, to).GroupBy(t => new { t.DepartmentId, t.SubBranchId }).Select(g => new { g.Key.DepartmentId, g.Key.SubBranchId, Count = g.Count() }).ToListAsync(ct);
        var closed = await ClosedIn(tickets, from, to).GroupBy(t => new { t.DepartmentId, t.SubBranchId }).Select(g => new { g.Key.DepartmentId, g.Key.SubBranchId, Count = g.Count() }).ToListAsync(ct);

        // The hours are the staff's: the branch and sub-branch of the person who worked, not those of the ticket (a ticket can be moved after the work was done).
        var rows = await _db.TicketWorkSessions.AsNoTracking()
            .Where(s => s.Ticket!.CompanyId == companyId && s.StartedAtUtc >= from && s.StartedAtUtc <= to)
            .Join(_db.Users, s => s.UserId, u => u.Id, (s, u) => new { u.DepartmentId, u.SubBranchId, s.StartedAtUtc, s.EndedAtUtc })
            .ToListAsync(ct);
        long Seconds(DateTime start, DateTime? end) => (end ?? now) > start ? (long)Math.Floor(((end ?? now) - start).TotalSeconds) : 0;

        static Place At(Guid? branch, Guid? sub) => new(branch ?? Guid.Empty, branch is null ? Guid.Empty : sub ?? Guid.Empty);
        var createdAt = created.GroupBy(c => At(c.DepartmentId, c.SubBranchId)).ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
        var closedAt = closed.GroupBy(c => At(c.DepartmentId, c.SubBranchId)).ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
        var secondsAt = rows.GroupBy(r => At(r.DepartmentId, r.SubBranchId)).ToDictionary(g => g.Key, g => g.Sum(r => Seconds(r.StartedAtUtc, r.EndedAtUtc)));

        var places = createdAt.Keys.Concat(closedAt.Keys).Concat(secondsAt.Keys).Distinct().ToList();
        var branchNames = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var subNames = await _db.SubBranches.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var total = created.Sum(c => c.Count);
        var items = places.GroupBy(p => p.Branch).Select(group =>
        {
            int Tickets(IEnumerable<Place> ps) => ps.Sum(p => createdAt.GetValueOrDefault(p));
            int Closed(IEnumerable<Place> ps) => ps.Sum(p => closedAt.GetValueOrDefault(p));
            long Work(IEnumerable<Place> ps) => ps.Sum(p => secondsAt.GetValueOrDefault(p));

            var inBranch = group.ToList();
            // Sub-branches are only spelled out for a branch that has some: one that has none has its figures as a whole.
            var subs = inBranch.Any(p => p.Sub != Guid.Empty)
                ? inBranch.Select(p => new SubBranchLoadDto(p.Sub == Guid.Empty ? null : p.Sub, p.Sub == Guid.Empty ? NoBranchLabel : subNames.GetValueOrDefault(p.Sub, "?"),
                        createdAt.GetValueOrDefault(p), closedAt.GetValueOrDefault(p), Math.Round(secondsAt.GetValueOrDefault(p) / 3600.0, 1)))
                    .OrderByDescending(x => x.Tickets).ThenByDescending(x => x.Hours).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
                : new List<SubBranchLoadDto>();
            var tickets = Tickets(inBranch);
            return new BranchLoadItemDto(group.Key == Guid.Empty ? null : group.Key, group.Key == Guid.Empty ? NoBranchLabel : branchNames.GetValueOrDefault(group.Key, "?"),
                tickets, Closed(inBranch), Math.Round(Work(inBranch) / 3600.0, 1), Share(tickets, total), subs);
        })
        .OrderByDescending(i => i.Tickets).ThenByDescending(i => i.Hours).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        return Outcome<BranchLoadDto>.Ok(new BranchLoadDto(total, Math.Round(secondsAt.Values.Sum() / 3600.0, 1), items));
    }
}
