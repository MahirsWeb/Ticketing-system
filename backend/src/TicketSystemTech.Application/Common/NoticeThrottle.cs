namespace TicketSystemTech.Application.Common;

/// <summary>
/// Lets a notice go to a person at most once in a given time. Anybody can lock somebody else's account by typing a wrong password
/// five times, every time the lock runs out; each lock sends the owner an e-mail, so the e-mails became a way to flood a mailbox.
/// Kept in memory (a restart forgets it, which at worst sends one extra e-mail), and small: entries older than the period are dropped.
/// </summary>
public sealed class NoticeThrottle
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DateTime> _lastSent = new();

    /// <summary>True, and remembered, when the person has not been sent this kind of notice within <paramref name="every"/>.</summary>
    public bool TryAcquire(Guid userId, DateTime nowUtc, TimeSpan every)
    {
        lock (_gate)
        {
            if (_lastSent.Count > 1000)
                foreach (var old in _lastSent.Where(x => nowUtc - x.Value >= every).Select(x => x.Key).ToList())
                    _lastSent.Remove(old);

            if (_lastSent.TryGetValue(userId, out var at) && nowUtc - at < every) return false;
            _lastSent[userId] = nowUtc;
            return true;
        }
    }
}
