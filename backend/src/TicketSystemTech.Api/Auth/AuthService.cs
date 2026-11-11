using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Middleware;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Api.Security;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Auth;

/// <summary>What a successful refresh hands the controller: the answer for the browser plus the next cookie's value.</summary>
public sealed record RefreshResult(LoginResponse Response, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

/// <summary>Signing in and the passwords: the temporary one, a forgotten one, the link that proves an e-mail address.</summary>
public interface IAuthService
{
    Task<Outcome<LoginResponse>> LoginAsync(LoginRequest request);

    /// <summary>Makes a new refresh token for a person who has just fully signed in, and returns its raw value (for the cookie)
    /// with its expiry. The database keeps only the hash.</summary>
    Task<(string RefreshToken, DateTime ExpiresAtUtc)> IssueRefreshTokenAsync(Guid userId);

    /// <summary>
    /// Trades the refresh cookie for a fresh access token (and a rotated refresh token). The presented token must be live:
    /// one that was already rotated away is treated as stolen and the person's every token is revoked. The person must still
    /// be welcome (active, not locked, no pending temporary password) — their claims are minted from the account as it is now.
    /// Every failure is the same 401, so the answer does not say which check refused.
    /// </summary>
    Task<Outcome<RefreshResult>> RefreshAsync(string? rawRefreshToken);

    /// <summary>Revokes the presented refresh token (sign-out). Unknown or absent tokens are ignored: signing out twice is fine.</summary>
    Task RevokeRefreshTokenAsync(string? rawRefreshToken);

    Task<Outcome<object>> SetNewPasswordAsync(SetNewPasswordRequest request);

    Task<Outcome<object>> VerifyEmailAsync(VerifyEmailRequest request);

    Task<Outcome<object>> ForgotPasswordAsync(ForgotPasswordRequest request);

    Task<Outcome<object>> ResetPasswordAsync(ResetPasswordRequest request);
}

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly FrontendOptions _frontendOptions;
    private readonly AppDbContext _db;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IMemoryCache _cache;
    private readonly IBackgroundEmailQueue _emailQueue;
    private readonly NoticeThrottle _lockoutNotices;
    private readonly NoticeThrottle _resetNotices;
    private readonly LoginAttemptLimiter _loginLimiter;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>At most one password-reset e-mail per account in this window, so a repeated request cannot flood a mailbox.</summary>
    private static readonly TimeSpan ResetEmailEvery = TimeSpan.FromMinutes(3);

    private readonly JwtOptions _jwt;

    public AuthService(
        UserManager<ApplicationUser> userManager, ITokenService tokenService, IOptions<FrontendOptions> frontendOptions,
        AppDbContext db, INotificationPublisher notificationPublisher, IMemoryCache cache, IBackgroundEmailQueue emailQueue,
        NoticeThrottle lockoutNotices, [FromKeyedServices("password-reset")] NoticeThrottle resetNotices, LoginAttemptLimiter loginLimiter, TimeProvider time,
        IOptions<JwtOptions> jwtOptions)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _frontendOptions = frontendOptions.Value;
        _jwt = jwtOptions.Value;
        _db = db;
        _notificationPublisher = notificationPublisher;
        _cache = cache;
        _emailQueue = emailQueue;
        _lockoutNotices = lockoutNotices;
        _resetNotices = resetNotices;
        _loginLimiter = loginLimiter;
        _time = time;
    }

    /// <summary>The one answer to every failed sign-in: wrong password, unknown email, inactive account or locked account.
    /// Telling a locked account apart ("423") confirmed that the email exists to anyone who made five wrong guesses.</summary>
    private const string InvalidCredentials =
        "Invalid email or password. After several failed attempts an account is locked for a few minutes.";

    // PBKDF2 takes noticeable time. A request for an unknown or locked account used to skip it and answer instantly, so
    // response time alone told "no such account" from "wrong password". These make those paths do the same work.
    private static readonly ApplicationUser DummyUser = new();
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<ApplicationUser>().HashPassword(DummyUser, "not-a-real-password"));

    private void SpendPasswordHashTime(string password) =>
        _userManager.PasswordHasher.VerifyHashedPassword(DummyUser, DummyHash.Value, password);

    public async Task<Outcome<LoginResponse>> LoginAsync(LoginRequest request)
    {
        // Counted per typed e-mail, existing account or not, so the answer is the same for both.
        if (!_loginLimiter.TryAcquire(request.Email))
        {
            return Outcome<LoginResponse>.Status(StatusCodes.Status429TooManyRequests, new { message = "Too many sign-in attempts for this account. Try again in a minute." }, new Dictionary<string, string> { ["Retry-After"] = "60" });
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            SpendPasswordHashTime(request.Password);
            return Outcome<LoginResponse>.Status(StatusCodes.Status401Unauthorized, new { message = InvalidCredentials });
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            SpendPasswordHashTime(request.Password);
            return Outcome<LoginResponse>.Status(StatusCodes.Status401Unauthorized, new { message = InvalidCredentials });
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user); // increments the failure count; locks the account out once the threshold is reached

            // The account's owner learns about it by email (to the address on file), not from the sign-in screen.
            // At most one such e-mail a day per person: otherwise anybody can lock the account again and again and fill its owner's mailbox.
            if (await _userManager.IsLockedOutAsync(user) && _lockoutNotices.TryAcquire(user.Id, UtcNow, TimeSpan.FromHours(24)))
            {
                var mail = AccountMessages.AccountLocked(user.FirstName);
                _emailQueue.Enqueue(user.Email!, mail.Subject, mail.Html);
            }

            return Outcome<LoginResponse>.Status(StatusCodes.Status401Unauthorized, new { message = InvalidCredentials });
        }
        await _userManager.ResetAccessFailedCountAsync(user);

        if (user.MustChangePassword)
        {
            if (user.TemporaryPasswordExpiresAtUtc is null || user.TemporaryPasswordExpiresAtUtc < UtcNow)
            {
                return Outcome<LoginResponse>.Status(StatusCodes.Status401Unauthorized, new
                {
                    code = "TEMP_PASSWORD_EXPIRED",
                    message = "Your temporary password has expired. Ask an admin or consultant to generate a new one."
                });
            }

            return Outcome<LoginResponse>.Ok(new LoginResponse(true, null, null, null));
        }

        if (user.Role == Domain.Enums.UserRole.Client && !user.EmailConfirmed)
        {
            return Outcome<LoginResponse>.Status(403, new { code = "EMAIL_NOT_VERIFIED", message = "Please verify your email address before logging in." });
        }

        var (token, expiresAt) = _tokenService.GenerateAccessToken(user.Id, user.Email!, user.FirstName, user.LastName, user.Role, user.CompanyId, user.DepartmentId, user.SecurityStamp ?? string.Empty);

        user.LastLoginAtUtc = UtcNow;
        await _userManager.UpdateAsync(user);

        return Outcome<LoginResponse>.Ok(new LoginResponse(false, token, expiresAt, await ToSummaryAsync(user)));
    }

    private const string TempPasswordRejected =
        "The temporary password is not valid or has expired. Ask an admin or consultant to generate a new one.";

    public async Task<Outcome<object>> SetNewPasswordAsync(SetNewPasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmNewPassword)
            return Outcome<object>.Invalid("New password and confirmation do not match.");

        var user = await _userManager.FindByEmailAsync(request.Email);

        // One answer for every way this can fail (no such account, inactive, no pending temporary password, expired,
        // locked, wrong password). Three different answers used to reveal which accounts had a live temporary password,
        // and the endpoint neither counted wrong guesses nor honoured the lockout, so it was an unthrottled guessing oracle.
        var usable = user is not null
            && user.IsActive
            && user.MustChangePassword
            && user.TemporaryPasswordExpiresAtUtc is not null
            && user.TemporaryPasswordExpiresAtUtc >= UtcNow
            && !await _userManager.IsLockedOutAsync(user);
        if (!usable)
        {
            SpendPasswordHashTime(request.TemporaryPassword);
            return Outcome<object>.Status(StatusCodes.Status400BadRequest, new { code = "TEMP_PASSWORD_INVALID", message = TempPasswordRejected });
        }

        var account = user!; // non-null: established by the check above
        var changeResult = await _userManager.ChangePasswordAsync(account, request.TemporaryPassword, request.NewPassword);
        if (!changeResult.Succeeded)
        {
            if (changeResult.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                await _userManager.AccessFailedAsync(account); // counted like a wrong sign-in, so it can lock the account too
                return Outcome<object>.Status(StatusCodes.Status400BadRequest, new { code = "TEMP_PASSWORD_INVALID", message = TempPasswordRejected });
            }

            // The temporary password was right; the NEW password broke a rule, which the person needs to be told.
            return Outcome<object>.Invalid(string.Join(" ", changeResult.Errors.Select(e => e.Description)));
        }
        await _userManager.ResetAccessFailedCountAsync(account);

        account.MustChangePassword = false;
        account.TemporaryPasswordExpiresAtUtc = null;
        await _userManager.UpdateAsync(account);

        await NotifyAdminsOfInviteAcceptedAsync(account);

        // Employees are trusted on creation and skip email verification; only clients need to verify.
        if (account.Role == Domain.Enums.UserRole.Client && !account.EmailConfirmed)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(account);
            var link = $"{_frontendOptions.BaseUrl}/verify-email?userId={account.Id}&token={Uri.EscapeDataString(token)}";
            var mail = AccountMessages.VerifyEmail(account.FirstName, link);
            // Filed and saved on the spot: the outbox row is the promise that the mail will be sent, and the worker delivers it.
            _db.StageEmail(account.Email!, mail.Subject, mail.Html);
            await _db.SaveChangesAsync();

            return Outcome<object>.Ok(new { message = "Password set. Please check your email to verify your account before logging in." });
        }

        return Outcome<object>.Ok(new { message = "Password set successfully. You can now log in." });
    }

    public async Task<Outcome<object>> VerifyEmailAsync(VerifyEmailRequest request)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Outcome<object>.Invalid("Invalid verification link.");

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return Outcome<object>.Invalid("This verification link is invalid or has expired.");

        return Outcome<object>.Ok(new { message = "Email verified. You can now log in." });
    }

    public async Task<Outcome<object>> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        // At most one reset e-mail per account per few minutes: repeated requests (a nervous click, or someone targeting a known
        // address) must not fill the owner's mailbox. The throttle gates only the e-mail; the response below is the same either way,
        // so it still does not reveal whether the address is registered.
        if (user is not null && user.IsActive && _resetNotices.TryAcquire(user.Id, UtcNow, ResetEmailEvery))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = $"{_frontendOptions.BaseUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            // Queued rather than awaited: only existing accounts get an email, so waiting for the send made the response time
            // reveal whether the address is registered.
            var mail = AccountMessages.PasswordReset(user.FirstName, link);
            _emailQueue.Enqueue(user.Email!, mail.Subject, mail.Html);
        }

        // Always return 200 so we don't leak which emails are registered.
        return Outcome<object>.Ok(new { message = "If that email exists in our system, a reset link has been sent." });
    }

    public async Task<Outcome<object>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmNewPassword)
            return Outcome<object>.Invalid("New password and confirmation do not match.");

        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Outcome<object>.Invalid("Invalid or expired reset link.");

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            return Outcome<object>.Invalid(string.Join(" ", result.Errors.Select(e => e.Description)));

        // The reset rotated the security stamp: drop the cached status so sessions opened with the old password die at once.
        _cache.Remove(UserStatusCache.Key(user.Id));
        // And drop every refresh token: regaining control of the mailbox must end any session a thief still holds.
        await RevokeFamilyAsync(user.Id);
        await _db.SaveChangesAsync();

        // The person just proved they control the account's mailbox, so a lockout caused by someone else's wrong guesses ends
        // here: it would otherwise be a way to keep a known account out for as long as an attacker cares to keep trying.
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        // A successful reset always supersedes any pending temporary-password requirement — otherwise a user
        // whose temp password expired would reset via email and still be blocked by the MustChangePassword check on login.
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            user.TemporaryPasswordExpiresAtUtc = null;
            await _userManager.UpdateAsync(user);
        }

        return Outcome<object>.Ok(new { message = "Password reset successfully. You can now log in." });
    }

    // ======================= refresh tokens =======================

    private const string RefreshRejected = "The session is no longer valid. Please sign in again.";

    private static string NewRawRefreshToken() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)); // 64 hex chars, cookie-safe

    /// <summary>SHA-256 of the raw value; what the database stores and looks tokens up by. A copy of the table replays nothing.</summary>
    private static string HashRefreshToken(string raw) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    public async Task<(string RefreshToken, DateTime ExpiresAtUtc)> IssueRefreshTokenAsync(Guid userId)
    {
        var raw = NewRawRefreshToken();
        var now = UtcNow;
        var expires = now.AddDays(Math.Max(_jwt.RefreshTokenDays, 1));
        var stamp = await _db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstOrDefaultAsync();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashRefreshToken(raw),
            ExpiresAtUtc = expires,
            SecurityStamp = stamp,
            SessionStartedAtUtc = now
        });

        // Housekeeping on the same trip: rows that can never be presented again (long expired, or long revoked) are dropped,
        // so the table holds live sessions and a short forensic tail, not history.
        var cutoff = now.AddDays(-7);
        await _db.RefreshTokens
            .Where(t => t.UserId == userId
                        && (t.ExpiresAtUtc < cutoff || (t.RevokedAtUtc != null && t.RevokedAtUtc < cutoff)))
            .ExecuteDeleteAsync();

        await _db.SaveChangesAsync();
        return (raw, expires);
    }

    /// <summary>What a rotation handed out, kept for the length of the grace window so that a second request that presents the SAME spent cookie
    /// is given the SAME successor (not a second, independent one).</summary>
    private sealed record Successor(string Raw, string Hash, DateTime ExpiresAtUtc);

    private static string SuccessorKey(string spentHash) => "refresh-successor:" + spentHash;

    // Presentations of one cookie are taken one at a time in this process, so that of two that arrive together the first rotates it and the second
    // finds the answer the first left. (The conditional UPDATE below still protects a second instance, which this does not reach.)
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<Outcome<RefreshResult>> RefreshAsync(string? rawRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return Outcome<RefreshResult>.Status(StatusCodes.Status401Unauthorized, new { message = RefreshRejected });

        var hash = HashRefreshToken(rawRefreshToken);
        var gate = Gates[(int)((uint)hash.GetHashCode() % (uint)Gates.Length)];
        await gate.WaitAsync();
        try
        {
            return await RefreshCoreAsync(hash);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Outcome<RefreshResult>> RefreshCoreAsync(string hash)
    {
        Outcome<RefreshResult> Reject() =>
            Outcome<RefreshResult>.Status(StatusCodes.Status401Unauthorized, new { message = RefreshRejected });

        var now = UtcNow;
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored is null) return Reject();

        // A token that was already rotated away is being presented again. Within a few seconds of the rotation that is two tabs (or two requests)
        // that raced on the same expired token: honest, and common (a browser restored with five tabs does exactly this). They are all given the
        // successor the first one got, so no second chain is made, and only while that successor is still alive: a sign-out, or the revoking of
        // the whole family, closes the window at once. Later than that it is a thief or an old copy being replayed: trust in the session is gone,
        // so every live token of the person is revoked and they sign in again.
        if (stored.RevokedAtUtc is not null)
        {
            var grace = TimeSpan.FromSeconds(Math.Max(_jwt.RefreshReuseGraceSeconds, 0));
            if (stored.ReplacedByTokenHash is null || now - stored.RevokedAtUtc.Value > grace)
            {
                if (stored.ReplacedByTokenHash is not null) // spent by a rotation, and presented again too late: the theft signal
                {
                    await RevokeFamilyAsync(stored.UserId);
                    await _db.SaveChangesAsync();
                }
                return Reject(); // signed out (nothing replaced it) or expired: refused, and nobody else is touched
            }

            if (!_cache.TryGetValue(SuccessorKey(hash), out Successor? successor) || successor is null) return Reject();
            var alive = await _db.RefreshTokens.AnyAsync(t => t.TokenHash == successor.Hash && t.RevokedAtUtc == null && t.ExpiresAtUtc > now);
            if (!alive) return Reject();

            var again = await WelcomeAsync(stored);
            return again is null ? Reject() : Outcome<RefreshResult>.Ok(await AnswerAsync(again, successor.Raw, successor.ExpiresAtUtc));
        }

        if (stored.ExpiresAtUtc <= now) return Reject();

        // However often it is renewed, a sign-in ends at an absolute date: a stolen cookie is not a permanent key.
        var sessionEnds = stored.SessionStartedAtUtc.AddDays(Math.Max(_jwt.SessionMaxDays, 1));
        if (sessionEnds <= now) return Reject();

        var user = await WelcomeAsync(stored);
        if (user is null)
        {
            stored.RevokedAtUtc = now;
            await _db.SaveChangesAsync();
            return Reject();
        }

        // Rotate: the presented token is spent, a fresh one takes its place, and the two are linked so a later replay of the old one is recognised
        // as the theft signal above. The claim is one conditional UPDATE: of two requests that present the same live token (in two instances of
        // the application) exactly one wins it, and the other is refused without anybody being revoked.
        var rawNext = NewRawRefreshToken();
        var nextHash = HashRefreshToken(rawNext);
        var expires = now.AddDays(Math.Max(_jwt.RefreshTokenDays, 1));
        if (expires > sessionEnds) expires = sessionEnds;

        var claimed = await _db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now).SetProperty(t => t.ReplacedByTokenHash, nextHash));
        if (claimed == 0) return Reject();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = nextHash,
            ExpiresAtUtc = expires,
            SecurityStamp = user.SecurityStamp,
            SessionStartedAtUtc = stored.SessionStartedAtUtc
        });
        await _db.SaveChangesAsync();

        var grace2 = TimeSpan.FromSeconds(Math.Max(_jwt.RefreshReuseGraceSeconds, 0));
        if (grace2 > TimeSpan.Zero) _cache.Set(SuccessorKey(hash), new Successor(rawNext, nextHash, expires), grace2);

        return Outcome<RefreshResult>.Ok(await AnswerAsync(user, rawNext, expires));
    }

    /// <summary>The person a refresh token belongs to, if they are still welcome: active, with no temporary password pending, and with the security
    /// stamp the token was made under. A token under an OLD stamp (the password was changed since) is revoked together with the other tokens of
    /// that person made under an old stamp, and only those: a session signed in after the change is not touched. A locked account is not refused:
    /// lockout stops new sign-ins with guessed passwords, and anybody who knows an address can cause it, so it must not throw out somebody who is
    /// already in.</summary>
    private async Task<ApplicationUser?> WelcomeAsync(RefreshToken stored)
    {
        var user = await _userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive || user.MustChangePassword) return null;

        if (stored.SecurityStamp is null || stored.SecurityStamp != user.SecurityStamp)
        {
            var stamp = user.SecurityStamp;
            await _db.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null && (t.SecurityStamp == null || t.SecurityStamp != stamp))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, UtcNow));
            return null;
        }

        return user;
    }

    private async Task<RefreshResult> AnswerAsync(ApplicationUser user, string rawRefreshToken, DateTime refreshExpiresAtUtc)
    {
        var (accessToken, accessExpires) = _tokenService.GenerateAccessToken(
            user.Id, user.Email!, user.FirstName, user.LastName, user.Role, user.CompanyId, user.DepartmentId, user.SecurityStamp ?? string.Empty);
        return new RefreshResult(new LoginResponse(false, accessToken, accessExpires, await ToSummaryAsync(user)), rawRefreshToken, refreshExpiresAtUtc);
    }

    public async Task RevokeRefreshTokenAsync(string? rawRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) return;
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == HashRefreshToken(rawRefreshToken));
        if (stored is null) return;

        // The token and whatever it was replaced by: a renewal that was under way (or answered a second tab in the grace window) while the person
        // signed out must not leave a live successor behind.
        var now = UtcNow;
        for (var step = 0; stored is not null && step < 20; step++)
        {
            stored.RevokedAtUtc ??= now;
            var next = stored.ReplacedByTokenHash;
            stored = next is null ? null : await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == next);
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>Revokes every still-live refresh token of one person (a theft signal, and reused on a password reset). Returns the count.</summary>
    private Task<int> RevokeFamilyAsync(Guid userId) => _db.RevokeRefreshTokensAsync(userId, UtcNow);

    private async Task<UserSummary> ToSummaryAsync(ApplicationUser user)
    {
        string? departmentName = user.DepartmentId.HasValue
            ? await _db.Departments.Where(d => d.Id == user.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync()
            : null;
        string? subBranchName = user.SubBranchId.HasValue
            ? await _db.SubBranches.Where(s => s.Id == user.SubBranchId).Select(s => s.Name).FirstOrDefaultAsync()
            : null;

        return new UserSummary(
            user.Id, user.FirstName, user.LastName, user.Email!, user.Role.ToString(),
            user.CompanyId, user.PhoneNumber, user.PhoneNumberPrompted, user.DepartmentId, departmentName,
            user.SubBranchId, subBranchName);
    }

    /// <summary>Tells every Admin that a user has just accepted their invite (set their own password for the first time).</summary>
    private async Task NotifyAdminsOfInviteAcceptedAsync(ApplicationUser activatedUser)
    {
        var message = $"{activatedUser.FirstName} {activatedUser.LastName} successfully logged in via invite — role: {activatedUser.Role}.";

        var admins = await _db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive).ToListAsync();
        foreach (var admin in admins)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = admin.Id,
                Type = NotificationType.UserActivatedViaInvite,
                Message = message
            });
        }
        await _db.SaveChangesAsync();
        await _notificationPublisher.PushToRoleAsync(UserRole.Admin, PushEvents.UserActivatedViaInvite, new { activatedUser.Id, activatedUser.FirstName, activatedUser.LastName, Role = activatedUser.Role.ToString() });
    }
}
