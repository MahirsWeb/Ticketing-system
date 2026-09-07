using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Infrastructure.Persistence;

/// <summary>Ensures an Admin account exists, using deployment-specific configuration rather than a
/// credential baked into source control — each environment (dev, a client's own instance, ...) sets
/// its own SeedAdmin:Email / SeedAdmin:Password.</summary>
public static class DbSeeder
{
    /// <summary>
    /// Only ensures an Admin account exists — a safety net so a freshly-deployed instance is never
    /// fully locked out. Does NOT seed any reference/lookup data (branches, help topics, SLA plans,
    /// categories) or the overdue-notification settings row; those are created on demand through the
    /// Settings UI (OverdueNotificationSettings is lazily created on first read/write in SettingsController).
    /// </summary>
    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        IConfiguration configuration,
        ITemporaryPasswordGenerator passwordGenerator,
        IOptions<TemporaryPasswordOptions> tempPasswordOptions)
    {
        var seedEmail = configuration["SeedAdmin:Email"];
        if (string.IsNullOrWhiteSpace(seedEmail))
        {
            logger.LogInformation("SeedAdmin:Email not configured — skipping initial Admin seed.");
            return;
        }

        var existingAdmin = await userManager.FindByEmailAsync(seedEmail);
        if (existingAdmin is not null) return;

        var configuredPassword = configuration["SeedAdmin:Password"];
        var wasGenerated = string.IsNullOrWhiteSpace(configuredPassword);
        var seedPassword = wasGenerated ? passwordGenerator.Generate() : configuredPassword!;

        var admin = new ApplicationUser
        {
            UserName = seedEmail,
            Email = seedEmail,
            EmailConfirmed = true,
            FirstName = "Admin",
            LastName = "User",
            Role = UserRole.Admin,
            IsActive = true,
            PhoneNumberPrompted = true,
            // Force a real password to be chosen on first login, same as the invite flow for
            // Employee/Client accounts — closes the exposure window regardless of how this
            // initial password was set (configured or auto-generated).
            MustChangePassword = true,
            TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(tempPasswordOptions.Value.ValidityMinutes)
        };

        var result = await userManager.CreateAsync(admin, seedPassword);
        if (!result.Succeeded)
        {
            logger.LogError("Failed to seed admin user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        logger.LogInformation("Seeded initial Admin account: {Email}", seedEmail);
        if (wasGenerated)
        {
            // Only ever surfaced here, once — not persisted anywhere in plain text.
            logger.LogWarning(
                "Generated initial password for {Email} (valid {Minutes} min, must be changed on first login): {Password}",
                seedEmail, tempPasswordOptions.Value.ValidityMinutes, seedPassword);
        }
    }
}
