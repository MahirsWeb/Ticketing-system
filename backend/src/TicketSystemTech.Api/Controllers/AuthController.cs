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
    private readonly IConfiguration _config;

    public AuthController(IAuthService auth, IConfiguration config)
    {
        _auth = auth;
        _config = config;
    }

    /// <summary>
    /// True when the request names an Origin that is neither this site nor one of <c>Cors:AllowedOrigins</c>. A browser always sends
    /// Origin on a cross-site POST, so a page on another site cannot sign the person out or spend their cookie. A request with no
    /// Origin (a script, a server) carries no browser cookie by accident and is left alone. SameSite=Strict already keeps the cookie
    /// off such a request; this is the second lock on the same door.
    /// </summary>
    private bool ForeignOrigin()
    {
        var origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return false;
        if (string.Equals(origin, $"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase)) return false;
        var allowed = _config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        return !allowed.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object ForeignOriginBody = new { message = "Request from an unexpected origin." };

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
            // A new sign-in replaces the one this browser still holds: the old cookie is revoked rather than left alive beside it.
            await _auth.RevokeRefreshTokenAsync(Request.Cookies[RefreshCookieName]);
            var (refreshToken, expiresAt) = await _auth.IssueRefreshTokenAsync(outcome.Value.User!.Id);
            SetRefreshCookie(refreshToken, expiresAt);
        }
        return this.ToActionResult(outcome);
    }

    /// <summary>Trades the refresh cookie for a fresh access token and rotates the cookie. No body: the cookie is the credential.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<ActionResult<LoginResponse>> Refresh()
    {
        if (ForeignOrigin()) return StatusCode(StatusCodes.Status403Forbidden, ForeignOriginBody);
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
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<IActionResult> Logout()
    {
        if (ForeignOrigin()) return StatusCode(StatusCodes.Status403Forbidden, ForeignOriginBody);
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
