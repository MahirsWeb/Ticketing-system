using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketSystemTech.Api.Auth;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Security;
using TicketSystemTech.Api.Services;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of signing in and the passwords. Every rule, and the careful sameness of the answers, is in <see cref="IAuthService"/>.</summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>
    /// The refresh token lives in an httpOnly cookie: script cannot read it, so an injected script cannot steal the session the
    /// way it could read a token from <c>localStorage</c>. The short access token is still returned in the body for the SPA to
    /// send as a Bearer header. <c>SameSite=Strict</c> keeps the cookie off cross-site requests (the SPA is same-site with the
    /// API in production), and <c>Path=/api/auth</c> keeps it off every other endpoint. <c>Secure</c> is set outside Development.
    /// </summary>
    private const string RefreshCookieName = "tst_refresh";

    private void SetRefreshCookie(string value, DateTime expiresUtc) =>
        Response.Cookies.Append(RefreshCookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = expiresUtc,
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/api/auth" });

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var outcome = await _auth.LoginAsync(request);
        // A full sign-in (not "you must set a password first") gets a refresh cookie for this browser.
        if (outcome.Succeeded && outcome.Value?.AccessToken is not null)
        {
            var (refreshToken, expiresAt) = await _auth.IssueRefreshTokenAsync(outcome.Value.User!.Id);
            SetRefreshCookie(refreshToken, expiresAt);
        }
        return this.ToActionResult(outcome);
    }

    /// <summary>Trades the refresh cookie for a fresh access token and rotates the cookie. No body: the cookie is the credential.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh()
    {
        var outcome = await _auth.RefreshAsync(Request.Cookies[RefreshCookieName]);
        if (!outcome.Succeeded)
        {
            ClearRefreshCookie(); // a refused cookie is a dead session: get it out of the browser
            return this.ToActionResult(outcome.Failure == Failure.Status
                ? Outcome<LoginResponse>.Status(outcome.StatusCode!.Value, outcome.Body!)
                : Outcome<LoginResponse>.Unauthorized());
        }

        SetRefreshCookie(outcome.Value!.RefreshToken, outcome.Value.RefreshTokenExpiresAtUtc);
        return Ok(outcome.Value.Response);
    }

    /// <summary>Signs out: revokes this session's refresh token and clears the cookie. Always succeeds.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _auth.RevokeRefreshTokenAsync(Request.Cookies[RefreshCookieName]);
        ClearRefreshCookie();
        return Ok();
    }

    [HttpPost("set-new-password")]
    public async Task<IActionResult> SetNewPassword(SetNewPasswordRequest request) =>
        this.ToResult(await _auth.SetNewPasswordAsync(request));

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request) =>
        this.ToResult(await _auth.VerifyEmailAsync(request));

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request) =>
        this.ToResult(await _auth.ForgotPasswordAsync(request));

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request) =>
        this.ToResult(await _auth.ResetPasswordAsync(request));
}
