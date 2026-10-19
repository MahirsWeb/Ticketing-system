using Microsoft.AspNetCore.SignalR;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Realtime;

/// <summary>
/// Real-time pushes are a convenience on top of rows that are already saved (the notification is in the database and
/// shows up on the next load). A failed push is logged and dropped; it must never fail the request that caused it.
/// </summary>
public class SignalRNotificationPublisher : INotificationPublisher
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly ILogger<SignalRNotificationPublisher> _logger;

    public SignalRNotificationPublisher(IHubContext<NotificationsHub> hub, ILogger<SignalRNotificationPublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task PushToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default)
        => SendAsync(GroupNames.User(userId.ToString()), eventName, payload, ct);

    public Task PushToRoleAsync(UserRole role, string eventName, object payload, CancellationToken ct = default)
        => SendAsync(GroupNames.Role(role.ToString()), eventName, payload, ct);

    private async Task SendAsync(string group, string eventName, object payload, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(group).SendAsync(eventName, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "SignalR push '{Event}' to {Group} failed", eventName, group);
        }
    }
}
