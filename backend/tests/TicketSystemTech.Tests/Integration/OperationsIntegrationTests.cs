using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Startup;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The operational findings against the real application and a real database: the way back in when the Admin has lost the password
/// (TS-53.2, TS-OPS-02.1), the password that must not reach a log, the ceiling per account on the real sign-in endpoint (TS-03.1),
/// and the key ring as it is actually stored in the database (TS-04.2, TS-HOST-02.1).
/// </summary>
[Collection(ApiCollection.Name)]
public class OperationsIntegrationTests
{
    private readonly ApiWorld _world;

    public OperationsIntegrationTests(ApiWorld world) => _world = world;

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@ops.test";

    private async Task<(Actor Admin, string Email)> NewAdminAsync(string key)
    {
        var admin = await _world.CreateActorAsync(key + Guid.NewGuid().ToString("N")[..6], UserRole.Admin, null, null);
        return (admin, admin.Email);
    }

    private async Task RemoveAsync(Guid userId)
    {
        await _world.Db(async db =>
        {
            await db.AuditLogs.Where(a => a.EntityId == userId).ExecuteDeleteAsync();
            return await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
        });
    }

    private async Task DeleteByEmailAsync(string email)
    {
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is { } user) await RemoveAsync(user.Id);
    }

    // ======================= the way back in =======================

    [SqlFact]
    public async Task The_reset_command_gives_a_lost_Admin_a_new_temporary_password_that_works_once_and_must_be_changed()
    {
        var (admin, email) = await NewAdminAsync("lostAdmin");
        try
        {
            // lock the account out first, as five wrong guesses would
            await _world.Db(db => db.Users.Where(u => u.Id == admin.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddHours(1)).SetProperty(u => u.AccessFailedCount, 5)));
            var output = new StringWriter();

            int code;
            using (var scope = _world.Factory.Services.CreateScope())
                code = await AdminPasswordReset.RunAsync(scope.ServiceProvider, email, output);

            Assert.Equal(0, code);
            var text = output.ToString();
            Assert.Contains(email, text);
            var temporary = text[(text.LastIndexOf(": ", StringComparison.Ordinal) + 2)..].Trim();
            Assert.True(temporary.Length >= 8);

            // the new password lets the Admin in, and only to choose a real one
            var login = await _world.Post(null, "/api/auth/login", new { email, password = temporary });
            Assert.Equal(200, login.Code);
            Assert.True(login.Json.GetProperty("requiresPasswordChange").GetBoolean());
            Assert.Equal(JsonValueKind.Null, login.Json.GetProperty("accessToken").ValueKind);

            // the old password is gone, and the old sessions with it
            Assert.Equal(401, (await _world.Post(null, "/api/auth/login", new { email, password = ApiWorld.Password })).Code);
            _world.ForgetCachedStatus(admin);
            Assert.Equal(401, (await _world.Get(admin, "/api/users")).Code);

            // the reset is on the audit trail, without the password
            var audit = Assert.Single(await _world.Db(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == admin.Id && a.Action == "AdminPasswordResetByCommand").ToListAsync()));
            Assert.DoesNotContain(temporary, audit.Details ?? "");
        }
        finally
        {
            await RemoveAsync(admin.Id);
        }
    }

    [SqlFact]
    public async Task The_reset_command_only_touches_Admin_accounts_and_says_so_otherwise()
    {
        var employee = await _world.CreateActorAsync("notAnAdmin" + Guid.NewGuid().ToString("N")[..6], UserRole.Employee, _world.BranchA, null);
        try
        {
            var output = new StringWriter();
            int unknown, notAdmin, noEmail;
            using (var scope = _world.Factory.Services.CreateScope())
            {
                unknown = await AdminPasswordReset.RunAsync(scope.ServiceProvider, Email("nobody"), output);
                notAdmin = await AdminPasswordReset.RunAsync(scope.ServiceProvider, employee.Email, output);
                noEmail = await AdminPasswordReset.RunAsync(scope.ServiceProvider, null, output);
            }

            Assert.Equal((1, 1, 2), (unknown, notAdmin, noEmail));
            // an employee kept their password: the old one still opens the door
            Assert.Equal(200, (await _world.Post(null, "/api/auth/login", new { email = employee.Email, password = ApiWorld.Password })).Code);
        }
        finally
        {
            await RemoveAsync(employee.Id);
        }
    }

    // ======================= the generated password =======================

    private async Task<(string Console, string Log, string Email)> SeedAdminAsync(bool printPassword)
    {
        var email = Email("seeded");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedAdmin:Email"] = email,
            ["SeedAdmin:PrintGeneratedPassword"] = printPassword.ToString()
        }).Build();
        var log = new CapturingLogger<OperationsIntegrationTests>();
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            using var scope = _world.Factory.Services.CreateScope();
            await DbSeeder.SeedAsync(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                log, configuration, scope.ServiceProvider.GetRequiredService<ITemporaryPasswordGenerator>(),
                scope.ServiceProvider.GetRequiredService<IOptions<TemporaryPasswordOptions>>());
        }
        finally
        {
            Console.SetOut(original);
        }
        return (console.ToString(), string.Join("\n", log.Lines), email);
    }

    [SqlFact]
    public async Task A_generated_Admin_password_is_never_written_to_the_log_and_is_not_shown_unless_asked_for()
    {
        var (console, log, email) = await SeedAdminAsync(printPassword: false);
        try
        {
            Assert.DoesNotContain("password for", console);                    // nothing on the terminal
            Assert.Contains("--reset-admin-password", log);                      // but the log says how to get one
            using var scope = _world.Factory.Services.CreateScope();
            var created = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
            Assert.NotNull(created); // the account exists whatever was shown; its password was generated and is not retrievable
        }
        finally
        {
            await DeleteByEmailAsync(email);
        }
    }

    [SqlFact]
    public async Task With_the_flag_the_generated_password_is_shown_once_on_the_terminal_and_still_not_in_the_log()
    {
        var (console, log, email) = await SeedAdminAsync(printPassword: true);
        try
        {
            Assert.Contains($"Generated initial password for {email}", console);
            var password = console[(console.LastIndexOf(": ", StringComparison.Ordinal) + 2)..].Trim();
            Assert.True(password.Length >= 8);
            Assert.DoesNotContain(password, log); // what goes through the logger goes to files too

            var login = await _world.Post(null, "/api/auth/login", new { email, password });
            Assert.Equal(200, login.Code);
            Assert.True(login.Json.GetProperty("requiresPasswordChange").GetBoolean());
        }
        finally
        {
            await DeleteByEmailAsync(email);
        }
    }

    // ======================= the ceiling per account =======================

    [SqlFact]
    public async Task A_sixth_sign_in_attempt_for_the_same_account_in_a_minute_is_refused_whether_or_not_the_account_exists()
    {
        // the test world allows 5 a minute per account (RateLimits:LoginPerAccountPerMinute)
        var existing = await _world.CreateActorAsync("limitedA" + Guid.NewGuid().ToString("N")[..6], UserRole.Employee, _world.BranchA, null);
        var other = await _world.CreateActorAsync("limitedB" + Guid.NewGuid().ToString("N")[..6], UserRole.Employee, _world.BranchA, null);
        try
        {
            foreach (var email in new[] { existing.Email, Email("never-existed") })
            {
                var codes = new List<int>();
                for (var i = 0; i < 6; i++) codes.Add((await _world.Post(null, "/api/auth/login", new { email, password = "wrong-" + i })).Code);

                Assert.Equal(new[] { 401, 401, 401, 401, 401, 429 }, codes);
            }

            var refused = await _world.Post(null, "/api/auth/login", new { email = existing.Email, password = ApiWorld.Password });
            Assert.Equal(429, refused.Code);
            Assert.Equal("60", refused.Header("Retry-After"));
            // another account is unaffected
            Assert.Equal(200, (await _world.Post(null, "/api/auth/login", new { email = other.Email, password = ApiWorld.Password })).Code);
        }
        finally
        {
            await RemoveAsync(existing.Id);
            await RemoveAsync(other.Id);
        }
    }

    // ======================= the key ring as stored =======================

    /// <summary>Starts the real application on a database of its own and returns what it stored in DataProtectionKeys.</summary>
    private static async Task<string> StoredKeyRingAsync(string? certificatePath, string? certificatePassword)
    {
        var database = TestSql.DatabasePrefix + "kr_" + Guid.NewGuid().ToString("N")[..8];
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = TestSql.ConnectionStringFor(database),
            ["Jwt__Secret"] = new string('s', 48),
            ["Pii__HashKey"] = new string('h', 48),
            ["Storage__UploadsPath"] = Path.Combine(Path.GetTempPath(), database),
            ["SeedAdmin__Email"] = "",
            ["Email__Outbox__Enabled"] = "false",
            ["DataProtection__CertificatePath"] = certificatePath ?? "",
            ["DataProtection__CertificatePassword"] = certificatePassword ?? ""
        };
        var saved = settings.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        foreach (var (name, value) in settings) Environment.SetEnvironmentVariable(name, value);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
            using var client = factory.CreateClient(); // builds and starts the host: migrations, the startup checks
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return string.Concat(await db.DataProtectionKeys.AsNoTracking().Select(k => k.Xml).ToListAsync());
        }
        finally
        {
            foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            using var master = TestSql.OpenMaster();
            using var cmd = master.CreateCommand();
            cmd.CommandText = $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
            cmd.ExecuteNonQuery();
            var uploads = Path.Combine(Path.GetTempPath(), database);
            if (Directory.Exists(uploads)) Directory.Delete(uploads, recursive: true);
        }
    }

    [SqlFact]
    public async Task With_a_certificate_the_keys_in_the_database_are_encrypted_and_without_one_they_are_in_the_clear()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tst-kr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var pfx = Path.Combine(folder, "keyring.pfx");
            TicketSystemTech.Api.Configuration.KeyRingProtection.CreateSelfSigned(pfx, "kr-password-1");

            var encrypted = await StoredKeyRingAsync(pfx, "kr-password-1");
            var clear = await StoredKeyRingAsync(null, null);

            Assert.False(string.IsNullOrEmpty(encrypted), "the application stored no key at all");
            Assert.Contains("EncryptedData", encrypted);
            Assert.DoesNotContain("<masterKey", encrypted);

            Assert.False(string.IsNullOrEmpty(clear));
            Assert.Contains("<masterKey", clear);   // what the tester's step 2 found: the key next to the data it protects
            Assert.DoesNotContain("EncryptedData", clear);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
