using Microsoft.EntityFrameworkCore;

namespace TicketSystemTech.Infrastructure.Persistence;

public static class RefreshTokenExtensions
{
    /// <summary>Ends every still-live sign-in of one person (all their browsers). Used when the account stops being welcome (deactivated, anonymised),
    /// when the password is reset by mail, and as the answer to a stolen cookie. Reactivating an account later must not bring an old cookie back to life.</summary>
    public static Task<int> RevokeRefreshTokensAsync(this AppDbContext db, Guid userId, DateTime nowUtc) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, nowUtc));
}
