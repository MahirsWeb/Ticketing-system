using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketSystemTech.Api.Auth;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of signing in and the passwords. Every rule, and the careful sameness of the answers, is in <see cref="IAuthService"/>.</summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request) =>
        this.ToActionResult(await _auth.LoginAsync(request));

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
