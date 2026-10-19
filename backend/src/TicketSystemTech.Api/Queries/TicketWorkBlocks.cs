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
    /// <paramref name="onlyBranch"/> restricts the tickets to one branch (null = no restriction).
    /// </summary>
    public static async Task<List<TicketWorkBlock>> ForUserAsync(AppDbContext db, Guid userId, DateTime from, DateTime to,
        Guid? onlyBranch, CancellationToken ct = default)
    {
        var logged = await db.TicketWorkSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.StartedAtUtc < to && (s.EndedAtUtc == null || s.EndedAtUtc >= from))
            .Where(s => onlyBranch == null || s.Ticket!.DepartmentId == onlyBranch)
            .Select(s => new TicketWorkBlock(s.TicketId, s.Ticket!.TicketNumber, s.Ticket.Title, s.Ticket.Status, s.StartedAtUtc, s.EndedAtUtc, true))
            .ToListAsync(ct);

        // A ticket nobody has logged any work on still appears for the person it is assigned to, from when it was opened until
        // it was closed, so a ticket does not vanish from the chart just because nobody typed in a time. It is not a measure
        // of effort (the export leaves its hours empty).
        var standIn = await db.Tickets.AsNoTracking()
            .Where(t => t.AssignedToUserId == userId && !t.WorkSessions.Any()
                && t.OpenedAtUtc != null && t.OpenedAtUtc < to && (t.ClosedAtUtc == null || t.ClosedAtUtc >= from))
            .Where(t => onlyBranch == null || t.DepartmentId == onlyBranch)
            .Select(t => new TicketWorkBlock(t.Id, t.TicketNumber, t.Title, t.Status, t.OpenedAtUtc!.Value, t.ClosedAtUtc, false))
            .ToListAsync(ct);

        return logged.Concat(standIn).OrderBy(b => b.Start).ToList();
    }
}
