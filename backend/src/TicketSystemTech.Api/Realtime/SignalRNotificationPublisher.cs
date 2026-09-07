using Microsoft.AspNetCore.SignalR;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Realtime;

public class SignalRNotificationPublisher : INotificationPublisher
{
    private readonly IHubContext<NotificationsHub> _hub;

    public SignalRNotificationPublisher(IHubContext<NotificationsHub> hub)
    {
        _hub = hub;
    }

    public Task PushToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default)
        => _hub.Clients.Group(GroupNames.User(userId.ToString())).SendAsync(eventName, payload, ct);

    public Task PushToRoleAsync(UserRole role, string eventName, object payload, CancellationToken ct = default)
        => _hub.Clients.Group(GroupNames.Role(role.ToString())).SendAsync(eventName, payload, ct);
}
