using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Queries;

/// <param name="IsLogged">True for real work sessions. False for the stand-in shown for a ticket nobody has logged any work on.</param>
public sealed record TicketWorkBlock(Guid TicketId, string TicketNumber, string Title, TicketStatus Status, DateTime Start, DateTime? End, bool IsLogged);

/// <summary>
/// "When did this person work on tickets": the one definition behind the Gantt chart and the timeline export.
///
/// Only a ticket's own worker has blocks. The people added to it as extra help are on the ticket, but they log no time
/// there, so they never show up on a timeline for it. Before work sessions existed, everyone assigned to a ticket was given the
/// whole of its working period, which counted a shared ticket once per person.
/// </summary>
public static class TicketWorkBlocks
{
    /// <summary>
    /// The blocks of <paramref name="userId"/> that touch the period [<paramref name="from"/>, <paramref name="to"/>).
    /// <paramref name="onlyBranch"/> restricts the tickets to one branch (null = no restriction). Something that has not ended
    /// lasts until <paramref name="nowUtc"/>, so on a day that has not happened yet there is nothing: a ticket that is open
    /// (or a session that is running) is not at work on every future day.
    /// </summary>
    public static async Task<List<TicketWorkBlock>> ForUserAsync(AppDbContext db, Guid userId, DateTime from, DateTime to,
        Guid? onlyBranch, DateTime nowUtc, CancellationToken ct = default) =>
        (await ForUsersAsync(db, new[] { userId }, from, to, onlyBranch, nowUtc, ct))[userId];

    /// <summary>
    /// The same for several people at once, in two queries whatever their number (the timeline export used to ask twice for each
    /// employee, so its cost grew with the staff). Everyone asked for has an entry, empty when they have no blocks.
    /// </summary>
    public static async Task<Dictionary<Guid, List<TicketWorkBlock>>> ForUsersAsync(AppDbContext db, IReadOnlyCollection<Guid> userIds,
        DateTime from, DateTime to, Guid? onlyBranch, DateTime nowUtc, CancellationToken ct = default)
    {
        var blocks = userIds.Distinct().ToDictionary(id => id, _ => new List<TicketWorkBlock>());
        if (blocks.Count == 0) return blocks;
        var ids = blocks.Keys.ToList();

        var logged = await db.TicketWorkSessions.AsNoTracking()
            .Where(s => ids.Contains(s.UserId) && s.StartedAtUtc < to && (s.EndedAtUtc ?? nowUtc) >= from)
            .Where(s => onlyBranch == null || s.Ticket!.DepartmentId == onlyBranch)
            .Select(s => new { s.UserId, s.TicketId, s.Ticket!.TicketNumber, s.Ticket.Title, s.Ticket.Status, s.StartedAtUtc, s.EndedAtUtc })
            .ToListAsync(ct);
        foreach (var s in logged)
            blocks[s.UserId].Add(new TicketWorkBlock(s.TicketId, s.TicketNumber, s.Title, s.Status, s.StartedAtUtc, s.EndedAtUtc, true));

        // A ticket nobody has logged any work on still appears for the person it is assigned to, from when it was opened until
        // it was closed, so a ticket does not vanish from the chart just because nobody typed in a time. It is not a measure
        // of effort (the export leaves its hours empty).
        var standIn = await db.Tickets.AsNoTracking()
            .Where(t => t.Assignees.Any(a => a.IsPrimary && ids.Contains(a.UserId)) && !t.WorkSessions.Any()
                && t.OpenedAtUtc != null && t.OpenedAtUtc < to && (t.ClosedAtUtc ?? nowUtc) >= from)
            .Where(t => onlyBranch == null || t.DepartmentId == onlyBranch)
            .Select(t => new
            {
                WorkerId = t.Assignees.Where(a => a.IsPrimary).Select(a => a.UserId).First(),
                t.Id,
                t.TicketNumber,
                t.Title,
                t.Status,
                Start = t.OpenedAtUtc!.Value,
                t.ClosedAtUtc,
            })
            .ToListAsync(ct);
        foreach (var t in standIn)
            blocks[t.WorkerId].Add(new TicketWorkBlock(t.Id, t.TicketNumber, t.Title, t.Status, t.Start, t.ClosedAtUtc, false));

        foreach (var id in ids) blocks[id] = blocks[id].OrderBy(b => b.Start).ToList();
        return blocks;
    }
}
