using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Integration;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Reporting;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Reports;

/// <summary>
/// The reports of the "Tickets" tab. Read only. Branches are isolated as in <see cref="IReportService"/>: staff only ever see their own branch, whatever
/// is asked; Admin sees all of them and may narrow to one. The transfers are the exception (see <see cref="TransfersAsync"/>).
/// </summary>
public interface ITicketInsightService
{
    /// <summary>Active tickets by age, without activity, without an assignee, due in 24 hours, overdue.</summary>
    Task<Outcome<AttentionDto>> AttentionAsync(Guid? departmentId, int inactiveDays, CancellationToken ct);

    Task<ResolutionTimeDto> ResolutionAsync(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct);

    Task<FirstResponseDto> FirstResponseAsync(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct);

    /// <summary>
    /// The transfers between branches made in the period. Staff see those where their own branch is the one the ticket left or the one it went to; Admin sees all,
    /// or those of one branch (either end) when a branch is asked for.
    /// </summary>
    Task<TransfersDto> TransfersAsync(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct);
}

public sealed class TicketInsightService : ITicketInsightService
{
    /// <summary>The days without activity that can be chosen.</summary>
    public static readonly int[] InactiveDayChoices = { 2, 3, 7 };

    /// <summary>The most tickets in one list; the list says how many there are in all.</summary>
    public const int MostListed = 20;

    public const int MostTransfersListed = 50;

    private static readonly TicketStatus[] Active = { TicketStatus.New, TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditTrailReader _audit;
    private readonly TimeProvider _time;

    public TicketInsightService(AppDbContext db, ICurrentUserService currentUser, IAuditTrailReader audit, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _time = time;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>The tickets of the branch of the person asking (Admin: all, or the one asked for).</summary>
    private IQueryable<Ticket> InScope(Guid? departmentId)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();
        if (_currentUser.Role != UserRole.Admin) return query.Where(t => t.DepartmentId == _currentUser.DepartmentId);
        return departmentId.HasValue ? query.Where(t => t.DepartmentId == departmentId.Value) : query;
    }

    private (DateTime From, DateTime To) Period(DateTime? from, DateTime? to)
    {
        var t = BusinessTimeZone.AsUtc(to ?? UtcNow);
        return (from.HasValue ? BusinessTimeZone.AsUtc(from.Value) : t.AddDays(-30), t);
    }

    private static double Round(double hours) => Math.Round(hours, 1);

    // ---------------------------------------------------------------- age and standstill

    public async Task<Outcome<AttentionDto>> AttentionAsync(Guid? departmentId, int inactiveDays, CancellationToken ct)
    {
        if (!InactiveDayChoices.Contains(inactiveDays)) return Outcome<AttentionDto>.Invalid("The days without activity must be 2, 3 or 7.");

        var now = UtcNow;
        var active = InScope(departmentId).Where(t => Active.Contains(t.Status));

        TicketBriefDto Brief(Ticket t, DateTime? last = null) =>
            new(t.Id, t.TicketNumber, t.Title, t.Status, t.CreatedAt, t.DueDateUtc, last, AgeDays(t.CreatedAt, now));

        // Age: the groups are counted by the database; the oldest ticket of a group is the first of it by creation time.
        var d2 = now.AddDays(-2);
        var d7 = now.AddDays(-7);
        var d30 = now.AddDays(-30);
        var counts = await active.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Up2 = g.Count(t => t.CreatedAt >= d2),
            Up7 = g.Count(t => t.CreatedAt < d2 && t.CreatedAt >= d7),
            Up30 = g.Count(t => t.CreatedAt < d7 && t.CreatedAt >= d30),
            Older = g.Count(t => t.CreatedAt < d30)
        }).FirstOrDefaultAsync(ct);

        async Task<AgeGroupDto> Group(string key, int count, IQueryable<Ticket> group)
        {
            if (count == 0) return new AgeGroupDto(key, 0, null);
            var oldest = await group.OrderBy(t => t.CreatedAt).ThenBy(t => t.TicketNumber).FirstAsync(ct);
            return new AgeGroupDto(key, count, Brief(oldest));
        }

        var ages = new List<AgeGroupDto>
        {
            await Group("upTo2Days", counts?.Up2 ?? 0, active.Where(t => t.CreatedAt >= d2)),
            await Group("upTo7Days", counts?.Up7 ?? 0, active.Where(t => t.CreatedAt < d2 && t.CreatedAt >= d7)),
            await Group("upTo30Days", counts?.Up30 ?? 0, active.Where(t => t.CreatedAt < d7 && t.CreatedAt >= d30)),
            await Group("older", counts?.Older ?? 0, active.Where(t => t.CreatedAt < d30))
        };

        var unassigned = await List(active.Where(t => !t.Assignees.Any()).OrderBy(t => t.CreatedAt), Brief, ct);
        var dueSoon = await List(active.Where(t => t.DueDateUtc != null && t.DueDateUtc >= now && t.DueDateUtc <= now.AddHours(24)).OrderBy(t => t.DueDateUtc), Brief, ct);
        var overdue = await List(active.Where(t => t.DueDateUtc != null && t.DueDateUtc < now).OrderBy(t => t.DueDateUtc), Brief, ct);

        return Outcome<AttentionDto>.Ok(new AttentionDto(now, counts?.Total ?? 0, ages, inactiveDays, await InactiveAsync(active, inactiveDays, now, Brief, ct), unassigned, dueSoon, overdue));
    }

    private static int AgeDays(DateTime createdAt, DateTime now) => Math.Max(0, (int)Math.Floor((now - createdAt).TotalDays));

    private static async Task<TicketBriefListDto> List(IQueryable<Ticket> ordered, Func<Ticket, DateTime?, TicketBriefDto> brief, CancellationToken ct)
    {
        var total = await ordered.CountAsync(ct);
        var items = await ordered.Take(MostListed).ToListAsync(ct);
        return new TicketBriefListDto(total, items.Select(t => brief(t, null)).ToList());
    }

    /// <summary>
    /// The last activity of a ticket is the latest of: its creation, its last message of type Response or InternalNote, its last audit row, its last assignment, and its
    /// last work session (a session that is still running is activity now). Only tickets older than the threshold can be idle for that long, so only they are looked at.
    /// </summary>
    private async Task<TicketBriefListDto> InactiveAsync(IQueryable<Ticket> active, int days, DateTime now, Func<Ticket, DateTime?, TicketBriefDto> brief, CancellationToken ct)
    {
        var cutoff = now.AddDays(-days);
        var rows = await active.Where(t => t.CreatedAt < cutoff).Select(t => new
        {
            t.Id,
            t.CreatedAt,
            Message = _db.TicketMessages.Where(m => m.TicketId == t.Id && (m.Type == MessageType.Response || m.Type == MessageType.InternalNote)).Max(m => (DateTime?)m.CreatedAt),
            Audit = _db.AuditLogs.Where(a => a.EntityType == nameof(Ticket) && a.EntityId == t.Id).Max(a => (DateTime?)a.CreatedAt),
            Assigned = t.Assignees.Max(a => (DateTime?)a.AssignedAtUtc),
            Session = t.WorkSessions.Max(s => (DateTime?)(s.EndedAtUtc ?? now))
        }).ToListAsync(ct);

        var idle = rows
            .Select(r => new { r.Id, Last = new[] { r.CreatedAt, r.Message ?? default, r.Audit ?? default, r.Assigned ?? default, r.Session ?? default }.Max() })
            .Where(r => r.Last < cutoff)
            .OrderBy(r => r.Last)
            .ToList();

        var shown = idle.Take(MostListed).ToList();
        var ids = shown.Select(s => s.Id).ToList();
        var tickets = await _db.Tickets.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        return new TicketBriefListDto(idle.Count, shown.Select(s => brief(tickets[s.Id], s.Last)).ToList());
    }

    // ---------------------------------------------------------------- how long it takes

    public async Task<ResolutionTimeDto> ResolutionAsync(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        // The same tickets as the average on the overview (SummaryAsync): closed in the period, with an opening time.
        var hours = (await InScope(departmentId)
                .Where(x => x.ClosedAtUtc != null && x.ClosedAtUtc >= f && x.ClosedAtUtc <= t && x.OpenedAtUtc != null)
                .Select(x => new { x.OpenedAtUtc, x.ClosedAtUtc }).ToListAsync(ct))
            .Select(x => (x.ClosedAtUtc!.Value - x.OpenedAtUtc!.Value).TotalHours).ToList();

        var d = Distribution.Of(hours);
        return new ResolutionTimeDto(d.Count, Round(d.Average), Round(d.Median), Round(d.P90));
    }

    public async Task<FirstResponseDto> FirstResponseAsync(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        var rows = await InScope(departmentId).Where(x => x.CreatedAt >= f && x.CreatedAt <= t).Select(x => new
        {
            x.CreatedAt,
            First = _db.TicketMessages
                .Where(m => m.TicketId == x.Id && m.Type == MessageType.Response
                    && _db.Users.Any(u => u.Id == m.AuthorId && (u.Role == UserRole.Employee || u.Role == UserRole.Admin)))
                .Min(m => (DateTime?)m.CreatedAt)
        }).ToListAsync(ct);

        var hours = rows.Where(r => r.First != null).Select(r => Math.Max(0, (r.First!.Value - r.CreatedAt).TotalHours)).ToList();
        var d = Distribution.Of(hours);
        return new FirstResponseDto(rows.Count, hours.Count, rows.Count - hours.Count, Round(d.Average), Round(d.Median), Round(d.P90));
    }

    // ---------------------------------------------------------------- transfers between branches

    public async Task<TransfersDto> TransfersAsync(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct)
    {
        var (f, t) = Period(from, to);
        // The branch the transfers are seen from: the staff's own, or the one Admin asked for (or none: all).
        Guid? seenFrom = _currentUser.Role == UserRole.Admin ? departmentId : _currentUser.DepartmentId;
        var isStaff = _currentUser.Role != UserRole.Admin;

        var all = await _audit.TransfersAsync(f, t.AddTicks(1), ct); // the end of the period is included, as everywhere in the reports
        var rows = all.Where(r => seenFrom is null && !isStaff || r.FromDepartmentId == seenFrom || r.ToDepartmentId == seenFrom).ToList();

        var names = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        string? Name(Guid? id) => id is { } k ? names.GetValueOrDefault(k, "?") : null;

        var edges = rows
            .GroupBy(r => (r.FromDepartmentId, r.ToDepartmentId))
            .Select(g => new TransferEdgeDto(g.Key.FromDepartmentId, Name(g.Key.FromDepartmentId), g.Key.ToDepartmentId, Name(g.Key.ToDepartmentId)!, g.Count()))
            .OrderByDescending(e => e.Count).ThenBy(e => e.FromName).ThenBy(e => e.ToName)
            .ToList();

        var shown = rows.OrderByDescending(r => r.AtUtc).Take(MostTransfersListed).ToList();
        var ids = shown.Select(r => r.TicketId).Distinct().ToList();
        var tickets = await _db.Tickets.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.TicketNumber, x.Title }).ToDictionaryAsync(x => x.Id, ct);
        var items = shown
            .Where(r => tickets.ContainsKey(r.TicketId))
            .Select(r => new TransferItemDto(r.TicketId, tickets[r.TicketId].TicketNumber, tickets[r.TicketId].Title, r.AtUtc,
                r.FromDepartmentId, Name(r.FromDepartmentId), r.ToDepartmentId, Name(r.ToDepartmentId)!))
            .ToList();

        return new TransfersDto(rows.Count, edges, items);
    }
}
