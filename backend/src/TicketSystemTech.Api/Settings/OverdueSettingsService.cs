using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Settings;

/// <summary>When the application warns about tickets that are late. There is one row of settings for the whole installation; it is made the first time it is asked for.</summary>
public interface IOverdueSettingsService
{
    Task<OverdueNotificationSettingsDto> GetAsync(CancellationToken ct);

    Task<OverdueNotificationSettingsDto> UpdateAsync(UpdateOverdueNotificationSettingsRequest request);
}

public sealed class OverdueSettingsService : IOverdueSettingsService
{
    private readonly AppDbContext _db;

    public OverdueSettingsService(AppDbContext db) => _db = db;

    public async Task<OverdueNotificationSettingsDto> GetAsync(CancellationToken ct)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        return new OverdueNotificationSettingsDto(settings.NotifyOnSlaBreach, settings.ManualOverdueDays);
    }

    public async Task<OverdueNotificationSettingsDto> UpdateAsync(UpdateOverdueNotificationSettingsRequest request)
    {
        var settings = await GetOrCreateSettingsAsync();
        settings.NotifyOnSlaBreach = request.NotifyOnSlaBreach;
        settings.ManualOverdueDays = request.ManualOverdueDays;
        await _db.SaveChangesAsync();
        return new OverdueNotificationSettingsDto(settings.NotifyOnSlaBreach, settings.ManualOverdueDays);
    }

    private async Task<OverdueNotificationSettings> GetOrCreateSettingsAsync(CancellationToken ct = default)
    {
        var settings = await _db.OverdueNotificationSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new OverdueNotificationSettings();
            _db.OverdueNotificationSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }
        return settings;
    }
}
