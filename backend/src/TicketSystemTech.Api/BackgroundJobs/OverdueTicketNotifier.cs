using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.BackgroundJobs;

/// <summary>
/// Periodically checks open tickets against the admin-configured overdue rules (SLA due date passed, and/or a
/// manual "open for N days" threshold) and emails every assignee once per ticket when it first goes overdue.
/// </summary>
public class OverdueTicketNotifier : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OverdueTicketNotifier> _logger;

    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public OverdueTicketNotifier(IServiceScopeFactory scopeFactory, ILogger<OverdueTicketNotifier> logger, TimeProvider time)
    {
        _time = time;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overdue ticket check failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <summary>What the job needs to know about a ticket: not the whole row (its HTML description, its messages), only what the warning says.</summary>
    private sealed record OverdueTicket(Guid Id, string TicketNumber, string Title, DateTime? DueDateUtc, DateTime? OpenedAtUtc);

    /// <summary>One pass: warns the assignees of every live ticket that is overdue and has not been warned about yet. Returns how many tickets it warned about.</summary>
    public async Task<int> CheckOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();

        var settings = await db.OverdueNotificationSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || (!settings.NotifyOnSlaBreach && settings.ManualOverdueDays is null))
            return 0;

        var now = UtcNow;
        var notifyOnSla = settings.NotifyOnSlaBreach;
        DateTime? openedBefore = settings.ManualOverdueDays is { } days ? now.AddDays(-days) : null;

        // The database picks the tickets; the job gets only the few columns the warning needs. It used to load every live ticket in
        // full every half hour and look at them one by one.
        var overdue = new List<(OverdueTicket Ticket, string Reason)>();
        var candidates = await db.Tickets.AsNoTracking()
            .Where(t => TicketWorkflow.ActiveStatuses.Contains(t.Status) && t.OverdueNotifiedAtUtc == null
                && ((notifyOnSla && t.DueDateUtc != null && t.DueDateUtc < now)
                    || (openedBefore != null && t.OpenedAtUtc != null && t.OpenedAtUtc < openedBefore)))
            .Select(t => new OverdueTicket(t.Id, t.TicketNumber, t.Title, t.DueDateUtc, t.OpenedAtUtc))
            .ToListAsync(ct);
        foreach (var t in candidates)
        {
            overdue.Add(notifyOnSla && t.DueDateUtc.HasValue && t.DueDateUtc.Value < now
                ? (t, $"it is past its SLA due date ({t.DueDateUtc.Value:yyyy-MM-dd HH:mm} UTC)")
                : (t, $"it has been open for more than {settings.ManualOverdueDays!.Value} day(s)"));
        }

        if (overdue.Count == 0) return 0;

        var warned = 0;
        foreach (var (ticket, reason) in overdue)
        {
            // Claim the ticket BEFORE telling anyone, with one conditional UPDATE. Marking it after sending meant a
            // failure half-way through re-sent every email already delivered on the next run, and two instances
            // running the same job each sent everything. Now only the run whose UPDATE changes the row sends; the
            // price is that a notification whose delivery fails is logged and not retried, which beats spamming.
            var claimed = await db.Tickets
                .Where(t => t.Id == ticket.Id && t.OverdueNotifiedAtUtc == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.OverdueNotifiedAtUtc, now), ct);
            if (claimed == 0) continue;

            var assigneeIds = await db.TicketAssignees.Where(a => a.TicketId == ticket.Id).Select(a => a.UserId).ToListAsync(ct);

            var assignees = await db.Users.Where(u => assigneeIds.Contains(u.Id) && u.IsActive).ToListAsync(ct);
            // Only people who can still act on it hear about it: the list, the e-mail and the push all skip inactive accounts.
            var warning = new TicketOverdueEvent(ticket.Id, ticket.TicketNumber, ticket.Title, reason, assignees);
            db.Notifications.AddRange(notifier.InApp(warning));
            await db.SaveChangesAsync(ct);

            await notifier.DeliverAsync(warning, ct);
            warned++;
        }

        _logger.LogInformation("Sent overdue notifications for {Count} ticket(s)", warned);
        return warned;
    }
}
