using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace TicketSystemTech.Api.Security;

/// <summary>
/// A ceiling on sign-in attempts PER ACCOUNT, next to the one per network address. The address limit alone is blind to an
/// attacker who comes from many addresses, and behind a proxy it is only as good as the proxy settings. This one counts what
/// was typed in the e-mail field, whether or not such an account exists, so it tells an attacker nothing about which do.
///
/// The ceiling (default 30 a minute, <c>RateLimits:LoginPerAccountPerMinute</c>) is far above what a person can do and far below
/// what guessing needs. The e-mail is kept only as a hash, and a counter that has been idle goes away by itself.
/// </summary>
public sealed class LoginAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public LoginAttemptLimiter(IConfiguration configuration)
    {
        var permits = Math.Max(1, configuration.GetValue("RateLimits:LoginPerAccountPerMinute", 30));
        _limiter = PartitionedRateLimiter.Create<string, string>(account =>
            RateLimitPartition.GetFixedWindowLimiter(account, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    }

    /// <summary>True when this attempt is allowed.</summary>
    public bool TryAcquire(string? email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        using var lease = _limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
