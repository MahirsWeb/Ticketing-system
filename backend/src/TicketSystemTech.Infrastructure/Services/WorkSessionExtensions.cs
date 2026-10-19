using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

public static class WorkSessionExtensions
{
    /// <summary>
    /// Ends the sessions that are still running on these tickets (only <paramref name="userId"/>'s, if given). Called wherever
    /// work on a ticket stops without anybody pressing "end": the ticket is closed, handed to another branch, or the person
    /// is taken off it. Without this a session would run on for ever and count days of work that never happened.
    /// The caller saves.
    /// </summary>
    public static async Task EndRunningSessionsAsync(this AppDbContext db, IReadOnlyCollection<Guid> ticketIds, Guid? userId,
        DateTime nowUtc, CancellationToken ct = default)
    {
        if (ticketIds.Count == 0) return;

        var running = await db.TicketWorkSessions
            .Where(s => s.EndedAtUtc == null && ticketIds.Contains(s.TicketId) && (userId == null || s.UserId == userId))
            .ToListAsync(ct);

        foreach (var session in running)
            session.EndedAtUtc = session.StartedAtUtc > nowUtc ? session.StartedAtUtc : nowUtc;
    }
}
