using System.Collections.Concurrent;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Stands in for the publisher of real-time events: nobody is connected in a test, so the real one reaches no one, and a push could not
/// be seen at all (T-47). It keeps what was pushed, to whom, and what it carried.
/// </summary>
public sealed class RecordingPublisher : INotificationPublisher
{
    public sealed record Push(string To, string Event, object Payload);

    private readonly ConcurrentQueue<Push> _sent = new();

    public IReadOnlyList<Push> Sent => _sent.ToArray();

    public void Clear() => _sent.Clear();

    public Task PushToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default)
    {
        _sent.Enqueue(new Push(userId.ToString(), eventName, payload));
        return Task.CompletedTask;
    }

    public Task PushToRoleAsync(UserRole role, string eventName, object payload, CancellationToken ct = default)
    {
        _sent.Enqueue(new Push("role:" + role, eventName, payload));
        return Task.CompletedTask;
    }
}
