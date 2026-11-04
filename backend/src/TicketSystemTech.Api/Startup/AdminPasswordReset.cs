using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Startup;

/// <summary>
/// <c>dotnet TicketSystemTech.Api.dll --reset-admin-password admin@firma.ba</c>: a way back in when the only Admin has lost the
/// password, and the supported replacement for editing the Users table by hand. It needs the same access the application has
/// (the connection string), prints the new temporary password once, to the terminal that ran it, and never to the log. The
/// account must change it at the next sign-in; any earlier sessions stop working, and a lockout is lifted.
/// </summary>
public static class AdminPasswordReset
{
    public static async Task<int> RunAsync(IServiceProvider services, string? email, TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            await output.WriteLineAsync("Usage: --reset-admin-password <email of the Admin account>");
            return 2;
        }

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<AppDbContext>();

        var admin = await users.FindByEmailAsync(email);
        if (admin is null || admin.Role != UserRole.Admin)
        {
            await output.WriteLineAsync("There is no Admin account with that e-mail address. (Other accounts are reset by an Admin in the Users screen.)");
            return 1;
        }

        var password = services.GetRequiredService<ITemporaryPasswordGenerator>().Generate();
        var minutes = services.GetRequiredService<IOptions<TemporaryPasswordOptions>>().Value.ValidityMinutes;

        var token = await users.GeneratePasswordResetTokenAsync(admin);
        var reset = await users.ResetPasswordAsync(admin, token, password);
        if (!reset.Succeeded)
        {
            await output.WriteLineAsync("The password could not be reset: " + string.Join(" ", reset.Errors.Select(e => e.Description)));
            return 1;
        }

        admin.MustChangePassword = true;
        admin.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(minutes);
        admin.IsActive = true;
        await users.UpdateAsync(admin);
        await users.ResetAccessFailedCountAsync(admin);
        await users.SetLockoutEndDateAsync(admin, null);
        await users.UpdateSecurityStampAsync(admin);

        db.Audit(null, "AdminPasswordResetByCommand", nameof(ApplicationUser), admin.Id, "From the command line; no password is recorded");
        await db.SaveChangesAsync();

        await output.WriteLineAsync($"New temporary password for {email} (valid {minutes} minutes, must be changed at the next sign-in): {password}");
        return 0;
    }
}
