using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Settings;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of the installation's settings (Admin only).</summary>
[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly IOverdueSettingsService _overdue;

    public SettingsController(IOverdueSettingsService overdue) => _overdue = overdue;

    [HttpGet("overdue-notifications")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<OverdueNotificationSettingsDto>> GetOverdueNotificationSettings(CancellationToken ct) =>
        Ok(await _overdue.GetAsync(ct));

    [HttpPut("overdue-notifications")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<OverdueNotificationSettingsDto>> UpdateOverdueNotificationSettings(UpdateOverdueNotificationSettingsRequest request) =>
        Ok(await _overdue.UpdateAsync(request));
}
