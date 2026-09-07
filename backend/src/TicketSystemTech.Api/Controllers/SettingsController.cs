using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("overdue-notifications")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<OverdueNotificationSettingsDto>> GetOverdueNotificationSettings()
    {
        var settings = await GetOrCreateSettingsAsync();
        return Ok(new OverdueNotificationSettingsDto(settings.NotifyOnSlaBreach, settings.ManualOverdueDays));
    }

    [HttpPut("overdue-notifications")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<OverdueNotificationSettingsDto>> UpdateOverdueNotificationSettings(UpdateOverdueNotificationSettingsRequest request)
    {
        var settings = await GetOrCreateSettingsAsync();
        settings.NotifyOnSlaBreach = request.NotifyOnSlaBreach;
        settings.ManualOverdueDays = request.ManualOverdueDays;
        await _db.SaveChangesAsync();
        return Ok(new OverdueNotificationSettingsDto(settings.NotifyOnSlaBreach, settings.ManualOverdueDays));
    }

    private async Task<OverdueNotificationSettings> GetOrCreateSettingsAsync()
    {
        var settings = await _db.OverdueNotificationSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new OverdueNotificationSettings();
            _db.OverdueNotificationSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }
}
