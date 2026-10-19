using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Api.Middleware;

/// <summary>Cache key shared between this middleware and anywhere a user's Active/Role/Department
/// changes (UsersController), so those endpoints can force an immediate re-check instead of waiting
/// out the cache TTL.</summary>
public static class UserStatusCache
{
    public static string Key(Guid userId) => $"user-status:{userId}";
}

/// <summary>
/// A JWT stays valid for its whole lifetime regardless of what happens to the account afterward —
/// deactivating a user, or moving them to a different branch/role, previously had no effect on a
/// token already in their browser until it naturally expired. This re-checks the account's current
/// Active/Role/Department against the token's claims on every authenticated request (short-lived
/// cache to avoid a DB round trip per request), together with the company and the security stamp (which
/// Identity rotates on every password change or reset, so those revoke all earlier sessions), and forces a 401 — which the frontend already treats
/// as "log out" — the moment they no longer match.
/// </summary>
public class UserStatusValidationMiddleware : IMiddleware
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(20);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public UserStatusValidationMiddleware(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private record LiveStatus(bool Exists, bool IsActive, UserRole Role, Guid? DepartmentId, Guid? CompanyId, string? SecurityStamp);

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var status = await _cache.GetOrCreateAsync(UserStatusCache.Key(userId), async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    var user = await _db.Users.AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => new { u.IsActive, u.Role, u.DepartmentId, u.CompanyId, u.SecurityStamp })
                        .FirstOrDefaultAsync();
                    return user is null
                        ? new LiveStatus(false, false, default, null, null, null)
                        : new LiveStatus(true, user.IsActive, user.Role, user.DepartmentId, user.CompanyId, user.SecurityStamp);
                });

                var tokenRole = principal.FindFirst(ClaimTypes.Role)?.Value;
                var tokenDepartmentId = principal.FindFirst("departmentId")?.Value;
                var tokenCompanyId = principal.FindFirst("companyId")?.Value;
                var tokenStamp = principal.FindFirst(JwtTokenService.SecurityStampClaim)?.Value;

                var roleStillMatches = status is not null && string.Equals(tokenRole, status.Role.ToString(), StringComparison.Ordinal);
                var departmentStillMatches = status is not null && status.DepartmentId?.ToString() == tokenDepartmentId;
                var companyStillMatches = status is not null && status.CompanyId?.ToString() == tokenCompanyId;
                // A token without the claim (issued before stamps existed) fails too: one forced re-login after deploy.
                var stampStillMatches = status is not null && tokenStamp is not null
                    && string.Equals(tokenStamp, status.SecurityStamp ?? string.Empty, StringComparison.Ordinal);

                if (status is null || !status.Exists || !status.IsActive || !roleStillMatches || !departmentStillMatches
                    || !companyStillMatches || !stampStillMatches)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { message = "Your session is no longer valid — please log in again." });
                    return;
                }
            }
        }

        await next(context);
    }
}
