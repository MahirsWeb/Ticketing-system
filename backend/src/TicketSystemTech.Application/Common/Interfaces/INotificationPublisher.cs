using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>Pushes real-time (SignalR) events. Persisting the Notification row is done separately by callers.</summary>
public interface INotificationPublisher
{
    Task PushToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default);
    Task PushToRoleAsync(UserRole role, string eventName, object payload, CancellationToken ct = default);
}
