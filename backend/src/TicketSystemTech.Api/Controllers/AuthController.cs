using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Middleware;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IEmailSender _emailSender;
    private readonly FrontendOptions _frontendOptions;
    private readonly AppDbContext _db;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IMemoryCache _cache;
    private readonly IBackgroundEmailQueue _emailQueue;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IEmailSender emailSender,
        IOptions<FrontendOptions> frontendOptions,
        AppDbContext db,
        INotificationPublisher notificationPublisher,
        IMemoryCache cache,
        IBackgroundEmailQueue emailQueue)
    {
        _cache = cache;
        _emailQueue = emailQueue;
        _userManager = userManager;
        _tokenService = tokenService;
        _emailSender = emailSender;
        _frontendOptions = frontendOptions.Value;
        _db = db;
        _notificationPublisher = notificationPublisher;
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

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            SpendPasswordHashTime(request.Password);
            return Unauthorized(new { message = InvalidCredentials });
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            SpendPasswordHashTime(request.Password);
            return Unauthorized(new { message = InvalidCredentials });
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user); // increments the failure count; locks the account out once the threshold is reached

            // The account's owner learns about it by email (to the address on file), not from the sign-in screen.
            if (await _userManager.IsLockedOutAsync(user))
                _emailQueue.Enqueue(user.Email!, "Your account was temporarily locked", EmailTemplates.AccountLocked(user.FirstName));

            return Unauthorized(new { message = InvalidCredentials });
        }
        await _userManager.ResetAccessFailedCountAsync(user);

        if (user.MustChangePassword)
        {
            if (user.TemporaryPasswordExpiresAtUtc is null || user.TemporaryPasswordExpiresAtUtc < DateTime.UtcNow)
            {
                return Unauthorized(new
                {
                    code = "TEMP_PASSWORD_EXPIRED",
                    message = "Your temporary password has expired. Ask an admin or consultant to generate a new one."
                });
            }

            return Ok(new LoginResponse(true, null, null, null));
        }

        if (user.Role == Domain.Enums.UserRole.Client && !user.EmailConfirmed)
        {
            return StatusCode(403, new { code = "EMAIL_NOT_VERIFIED", message = "Please verify your email address before logging in." });
        }

        var (token, expiresAt) = _tokenService.GenerateAccessToken(user.Id, user.Email!, user.FirstName, user.LastName, user.Role, user.CompanyId, user.DepartmentId, user.SecurityStamp ?? string.Empty);

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return Ok(new LoginResponse(false, token, expiresAt, await ToSummaryAsync(user)));
    }

    private const string TempPasswordRejected =
        "The temporary password is not valid or has expired. Ask an admin or consultant to generate a new one.";

    [HttpPost("set-new-password")]
    public async Task<IActionResult> SetNewPassword(SetNewPasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmNewPassword)
            return BadRequest(new { message = "New password and confirmation do not match." });

        var user = await _userManager.FindByEmailAsync(request.Email);

        // One answer for every way this can fail (no such account, inactive, no pending temporary password, expired,
        // locked, wrong password). Three different answers used to reveal which accounts had a live temporary password,
        // and the endpoint neither counted wrong guesses nor honoured the lockout, so it was an unthrottled guessing oracle.
        var usable = user is not null
            && user.IsActive
            && user.MustChangePassword
            && user.TemporaryPasswordExpiresAtUtc is not null
            && user.TemporaryPasswordExpiresAtUtc >= DateTime.UtcNow
            && !await _userManager.IsLockedOutAsync(user);
        if (!usable)
        {
            SpendPasswordHashTime(request.TemporaryPassword);
            return BadRequest(new { code = "TEMP_PASSWORD_INVALID", message = TempPasswordRejected });
        }

        var account = user!; // non-null: established by the check above
        var changeResult = await _userManager.ChangePasswordAsync(account, request.TemporaryPassword, request.NewPassword);
        if (!changeResult.Succeeded)
        {
            if (changeResult.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                await _userManager.AccessFailedAsync(account); // counted like a wrong sign-in, so it can lock the account too
                return BadRequest(new { code = "TEMP_PASSWORD_INVALID", message = TempPasswordRejected });
            }

            // The temporary password was right; the NEW password broke a rule, which the person needs to be told.
            return BadRequest(new { message = string.Join(" ", changeResult.Errors.Select(e => e.Description)) });
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
            await _emailSender.SendAsync(account.Email!, "Please verify your email address",
                EmailTemplates.VerifyEmail(account.FirstName, link));

            return Ok(new { message = "Password set. Please check your email to verify your account before logging in." });
        }

        return Ok(new { message = "Password set successfully. You can now log in." });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return BadRequest(new { message = "Invalid verification link." });

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return BadRequest(new { message = "This verification link is invalid or has expired." });

        return Ok(new { message = "Email verified. You can now log in." });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is not null && user.IsActive)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = $"{_frontendOptions.BaseUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            // Queued rather than awaited: only existing accounts get an email, so waiting for the send made the response time
            // reveal whether the address is registered.
            _emailQueue.Enqueue(user.Email!, "Reset your password", EmailTemplates.ForgotPassword(user.FirstName, link));
        }

        // Always return 200 so we don't leak which emails are registered.
        return Ok(new { message = "If that email exists in our system, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmNewPassword)
            return BadRequest(new { message = "New password and confirmation do not match." });

        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return BadRequest(new { message = "Invalid or expired reset link." });

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        // The reset rotated the security stamp: drop the cached status so sessions opened with the old password die at once.
        _cache.Remove(UserStatusCache.Key(user.Id));

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

        return Ok(new { message = "Password reset successfully. You can now log in." });
    }

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
        await _notificationPublisher.PushToRoleAsync(UserRole.Admin, "userActivatedViaInvite", new { activatedUser.Id, activatedUser.FirstName, activatedUser.LastName, Role = activatedUser.Role.ToString() });
    }
}
