using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

public static class WorkSessionExtensions
{
    /// <summary>
    /// Every session this person has on tickets and tasks other than the given ones: what a new or changed session has to stay clear
    /// of (<see cref="TicketSystemTech.Application.WorkTime.WorkSessionRules.ValidateAgainstOtherWork"/>).
    /// </summary>
    public static async Task<List<TicketSystemTech.Application.WorkTime.WorkSessionRules.Span>> OtherWorkOfAsync(this AppDbContext db, Guid userId,
        Guid? exceptTicketId, Guid? exceptTaskId, CancellationToken ct = default)
    {
        var onTickets = await db.TicketWorkSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.TicketId != exceptTicketId)
            .Select(s => new TicketSystemTech.Application.WorkTime.WorkSessionRules.Span(s.Id, s.StartedAtUtc, s.EndedAtUtc))
            .ToListAsync(ct);
        var onTasks = await db.WorkTaskSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.WorkTaskId != exceptTaskId)
            .Select(s => new TicketSystemTech.Application.WorkTime.WorkSessionRules.Span(s.Id, s.StartedAtUtc, s.EndedAtUtc))
            .ToListAsync(ct);
        return onTickets.Concat(onTasks).ToList();
    }

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
