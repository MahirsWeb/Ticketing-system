using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
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

    public OverdueTicketNotifier(IServiceScopeFactory scopeFactory, ILogger<OverdueTicketNotifier> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOverdueTicketsAsync(stoppingToken);
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

    private async Task CheckOverdueTicketsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var notificationPublisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();

        var settings = await db.OverdueNotificationSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || (!settings.NotifyOnSlaBreach && settings.ManualOverdueDays is null))
            return;

        var now = DateTime.UtcNow;
        var candidates = await db.Tickets
            .Where(t => (t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress) && t.OverdueNotifiedAtUtc == null)
            .ToListAsync(ct);

        var overdue = new List<(Ticket Ticket, string Reason)>();
        foreach (var t in candidates)
        {
            if (settings.NotifyOnSlaBreach && t.DueDateUtc.HasValue && t.DueDateUtc.Value < now)
            {
                overdue.Add((t, $"it is past its SLA due date ({t.DueDateUtc.Value:yyyy-MM-dd HH:mm} UTC)"));
                continue;
            }
            if (settings.ManualOverdueDays.HasValue && t.OpenedAtUtc.HasValue && t.OpenedAtUtc.Value.AddDays(settings.ManualOverdueDays.Value) < now)
            {
                overdue.Add((t, $"it has been open for more than {settings.ManualOverdueDays.Value} day(s)"));
            }
        }

        if (overdue.Count == 0) return;

        foreach (var (ticket, reason) in overdue)
        {
            var assigneeIds = await db.TicketAssignees.Where(a => a.TicketId == ticket.Id).Select(a => a.UserId).ToListAsync(ct);
            if (assigneeIds.Count == 0 && ticket.AssignedToUserId.HasValue) assigneeIds.Add(ticket.AssignedToUserId.Value);

            var assignees = await db.Users.Where(u => assigneeIds.Contains(u.Id) && u.IsActive).ToListAsync(ct);
            foreach (var assignee in assignees)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = assignee.Id,
                    Type = NotificationType.TicketOverdue,
                    Message = $"Ticket #{ticket.TicketNumber} is overdue",
                    TicketId = ticket.Id
                });
                await emailSender.SendAsync(assignee.Email!, $"Ticket #{ticket.TicketNumber} is overdue",
                    EmailTemplates.TicketOverdue(assignee.FirstName, ticket.TicketNumber, ticket.Title, reason));
            }

            ticket.OverdueNotifiedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);

        foreach (var (ticket, _) in overdue)
        {
            var assigneeIds = await db.TicketAssignees.AsNoTracking().Where(a => a.TicketId == ticket.Id).Select(a => a.UserId).ToListAsync(ct);
            foreach (var userId in assigneeIds)
                await notificationPublisher.PushToUserAsync(userId, "ticketOverdue", new { ticket.Id, ticket.TicketNumber, ticket.Title });
        }

        _logger.LogInformation("Sent overdue notifications for {Count} ticket(s)", overdue.Count);
    }
}
