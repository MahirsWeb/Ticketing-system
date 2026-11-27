using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Api.Startup;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>--import-package against the real (throwaway) database: what is written, how, and what is NOT done (nobody is told, no password is made).</summary>
[Collection(ApiCollection.Name)]
public class LegacyImportTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTime Start = new(2021, 5, 4, 9, 30, 0, DateTimeKind.Utc);

    private const string OldPassword = "Stara-lozinka-123";

    private readonly ApiWorld _world;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "import-" + Guid.NewGuid().ToString("N"));
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public LegacyImportTests(ApiWorld world)
    {
        _world = world;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private void WriteLines<T>(string file, IEnumerable<T> rows) => File.WriteAllLines(Path.Combine(_folder, file), rows.Select(r => JsonSerializer.Serialize(r, Json)));

    private string Email(string who) => $"{who}.{_tag}@example.test";

    // What PHP makes of a password: $2y$. (BCrypt.Net writes $2a$, the same hash.) Cost 4 keeps the test fast.
    private readonly string _oldHash = BCrypt.Net.BCrypt.HashPassword(OldPassword, 4).Replace("$2a$", "$2y$");

    /// <summary>Two companies, one branch, two clients, one employee, three tickets (one with a script in it, one open and overdue).</summary>
    private void WritePackage(Func<PackageTicket, PackageTicket>? change = null)
    {
        change ??= t => t;
        WriteLines("companies.jsonl", new[]
        {
            new PackageCompany("o:1", "Prva firma " + _tag, "Ulica 1, Sarajevo", "Telefon: 033 000 000", false),
            new PackageCompany("g:n", "Neodređeni " + _tag, null, null, false),
        });
        WriteLines("departments.jsonl", new[] { new PackageDepartment("d:1", "Softver " + _tag, $"software.{_tag}@example.test") });
        WriteLines("helptopics.jsonl", new[] { new PackageLookup("t:1", "Greška " + _tag) });
        WriteLines("slaplans.jsonl", new[] { new PackageSlaPlan("l:1", "Default " + _tag, 48) });
        WriteLines("categories.jsonl", new[] { new PackageLookup("k:f", "Fakturisano " + _tag) });
        WriteLines("users.jsonl", new[]
        {
            new PackageUser("c:1", "Client", Email("ana"), "Ana", "Anić", "061 111 222", "o:1", null, true, Start, _oldHash, true),
            new PackageUser("c:2", "Client", Email("dino"), "Dino", "Dinić", null, "g:n", null, true, Start),
            new PackageUser("s:1", "Employee", Email("boris"), "Boris", "Borić", null, null, "d:1", true, Start, _oldHash),
        });

        var closed = new PackageTicket(
            "LG" + _tag + "-1", "Prvi tiket", "<p>Ne radi štampač.</p><script>alert('x')</script>", "Closed", "Email", "Medium", "c:1", "o:1", "d:1", "t:1", "l:1", "k:f",
            Start, Start.AddHours(1), Start.AddDays(2), Start.AddDays(1), "s:1", "Zamijenjen toner.", [new("s:1", Start.AddHours(1), true)],
            [new("s:1", "Response", "<p>Provjeravamo.</p>", Start.AddHours(2)), new("s:1", "InternalNote", "<p>Toner prazan.</p>", Start.AddHours(3)), new("c:1", "Response", "<p>Hvala.</p>", Start.AddHours(4))]);
        var open = closed with
        {
            Number = "LG" + _tag + "-2",
            Title = "Drugi tiket",
            Status = "Open",
            ClosedAtUtc = null,
            ClosedByKey = null,
            ResolutionSummary = null,
            DueDateUtc = Start.AddDays(3),
            CategoryKey = null,
            Messages = [],
        };
        var undetermined = closed with { Number = "LG" + _tag + "-3", Title = "Treći tiket", ClientKey = "c:2", CompanyKey = "g:n", Messages = [], Assignees = [] };
        WriteLines("tickets.jsonl", new[] { change(closed), change(open), change(undetermined) });
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _world.Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task CleanUp()
    {
        await _world.Db(async db =>
        {
            var numbers = new[] { "LG" + _tag + "-1", "LG" + _tag + "-2", "LG" + _tag + "-3" };
            var companies = await db.Companies.Where(c => c.Name.EndsWith(_tag)).Select(c => c.Id).ToListAsync();
            var departments = await db.Departments.Where(d => d.Name.EndsWith(_tag)).Select(d => d.Id).ToListAsync();
            db.Tickets.RemoveRange(db.Tickets.Where(t => numbers.Contains(t.TicketNumber))); // the messages and the assignees go with their tickets
            await db.SaveChangesAsync();
            await db.Users.Where(u => (u.CompanyId != null && companies.Contains(u.CompanyId.Value)) || (u.DepartmentId != null && departments.Contains(u.DepartmentId.Value))).ExecuteDeleteAsync();
            await db.Companies.Where(c => companies.Contains(c.Id)).ExecuteDeleteAsync();
            await db.Departments.Where(d => departments.Contains(d.Id)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => h.Name.EndsWith(_tag)).ExecuteDeleteAsync();
            await db.SlaPlans.Where(s => s.Name.EndsWith(_tag)).ExecuteDeleteAsync();
            await db.TicketCategories.Where(c => c.Name.EndsWith(_tag)).ExecuteDeleteAsync();
            await db.AuditLogs.Where(a => a.Action == "LegacyImportCompleted" && a.Details!.Contains("tickets=3")).ExecuteDeleteAsync();
            return 0;
        });
    }

    [SqlFact]
    public async Task A_package_is_written_through_the_application_and_nobody_is_told()
    {
        WritePackage();
        var notices = await _world.Db(async db => (await db.Notifications.CountAsync(), await db.EmailOutbox.CountAsync()));
        try
        {
            var result = await InScope(sp => LegacyImport.RunCoreAsync(sp, _folder, dryRun: false, requireEmptyDatabase: false, restartNumbers: false, TextWriter.Null,
                TestContext.Current.CancellationToken));

            Assert.Empty(result.Problems);
            Assert.Equal(3, result.Tickets);
            Assert.Equal(3, result.Messages);
            Assert.Equal(3, result.UsersCreated);
            Assert.Equal(0, result.TicketsOfAnotherCompanyThanTheirClient);

            // Nobody was told: no notice, no e-mail.
            Assert.Equal(notices, await _world.Db(async db => (await db.Notifications.CountAsync(), await db.EmailOutbox.CountAsync())));

            // The address is encrypted at rest, found again through its hash, and nobody has a password.
            using var scope = _world.Factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var protector = scope.ServiceProvider.GetRequiredService<IPiiProtector>();
            var ana = await users.FindByEmailAsync(Email("ana"));
            Assert.NotNull(ana);
            Assert.Equal(_oldHash, ana!.PasswordHash); // the hash of the old system, as it was
            Assert.False(ana.MustChangePassword); // a person with a password has nothing to change
            Assert.True(ana.EmailConfirmed); // the old system had confirmed it
            var dino = await users.FindByEmailAsync(Email("dino"));
            Assert.Null(dino!.PasswordHash); // no password there, none here: an administrator gives one
            Assert.True(dino.MustChangePassword);
            Assert.False(dino.EmailConfirmed);
            Assert.Equal(2, result.PeopleWithTheirOldPassword);
            Assert.Equal(UserRole.Client, ana.Role);
            Assert.Equal("bs", ana.PreferredLanguage);
            Assert.Equal(Start, ana.CreatedAtUtc);
            var stored = await _world.Db(db => db.Database.SqlQueryRaw<string>("SELECT [Email] AS [Value] FROM [AspNetUsers] WHERE [Id] = {0}", ana.Id).SingleAsync());
            Assert.True(protector.IsProtected(stored));
            Assert.DoesNotContain("@", stored);

            await _world.Db(async db =>
            {
                var tickets = await db.Tickets.AsNoTracking().Include(t => t.Messages).Include(t => t.Assignees).Where(t => t.TicketNumber.StartsWith("LG" + _tag)).OrderBy(t => t.TicketNumber).ToListAsync();
                Assert.Equal(3, tickets.Count);

                // The text is cleaned by the application's own sanitizer, and the date of the old system is kept.
                var first = tickets[0];
                Assert.DoesNotContain("<script", first.Description);
                Assert.Contains("Ne radi štampač.", first.Description);
                Assert.Equal(Start, first.CreatedAt);
                Assert.Equal(TicketStatus.Closed, first.Status);
                Assert.Equal(TicketSource.Email, first.Source);
                Assert.Equal(TicketPriority.Medium, first.Priority);
                Assert.Equal("Zamijenjen toner.", first.ResolutionSummary);
                Assert.NotNull(first.ClosedByUserId);
                Assert.NotNull(first.CategoryId);
                Assert.Equal(new[] { MessageType.Response, MessageType.InternalNote, MessageType.Response }, first.Messages.OrderBy(m => m.CreatedAt).Select(m => m.Type));
                var primary = Assert.Single(first.Assignees);
                Assert.True(primary.IsPrimary);
                Assert.Equal(Start.AddHours(1), primary.AssignedAtUtc);

                // An old deadline does not make the overdue job write about it on the first day.
                Assert.NotNull(tickets[1].OverdueNotifiedAtUtc);

                // Every ticket is in the company of the person who wrote it, the undetermined one in the group made for it.
                foreach (var t in tickets)
                {
                    var client = await db.Users.AsNoTracking().SingleAsync(u => u.Id == t.ClientId);
                    Assert.Equal(client.CompanyId, t.CompanyId);
                }
                var group = await db.Companies.AsNoTracking().SingleAsync(c => c.Id == tickets[2].CompanyId);
                Assert.Equal("Neodređeni " + _tag, group.Name);
                Assert.False(group.AiIndexingEnabled);

                Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "LegacyImportCompleted" && a.Details!.Contains("tickets=3")));
                return 0;
            });
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_person_who_brought_the_old_password_signs_in_with_it_and_that_sign_in_replaces_the_hash()
    {
        WritePackage();
        try
        {
            await InScope(sp => LegacyImport.RunCoreAsync(sp, _folder, dryRun: false, requireEmptyDatabase: false, restartNumbers: false, TextWriter.Null,
                TestContext.Current.CancellationToken));

            Task<ApiResponse> SignIn(string who, string password) => _world.Send(null, HttpMethod.Post, "/api/auth/login", new { email = Email(who), password });

            Assert.Equal(401, (await SignIn("ana", OldPassword + "x")).Code); // a wrong password is a wrong password

            var login = await SignIn("ana", OldPassword);
            Assert.Equal(200, login.Code);
            Assert.False(login.Json.GetProperty("requiresPasswordChange").GetBoolean()); // nothing to change: it is her own password
            Assert.False(string.IsNullOrEmpty(login.Json.GetProperty("accessToken").GetString()));

            using var scope = _world.Factory.Services.CreateScope();
            var ana = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(Email("ana"));
            Assert.False(LegacyPasswordHasher.IsBcrypt(ana!.PasswordHash)); // replaced by that sign-in with Identity's own
            Assert.NotEqual(_oldHash, ana.PasswordHash);

            Assert.Equal(200, (await SignIn("ana", OldPassword)).Code); // and the same password goes on working
            Assert.Equal(200, (await SignIn("boris", OldPassword)).Code); // a member of the staff the same way
            Assert.Equal(401, (await SignIn("dino", OldPassword)).Code); // a person who had none cannot sign in until an administrator gives one
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_dry_run_checks_and_counts_and_writes_nothing()
    {
        WritePackage();
        var before = await _world.Db(async db => (await db.Companies.CountAsync(), await db.Tickets.CountAsync(), await db.Users.CountAsync()));
        try
        {
            var result = await InScope(sp => LegacyImport.RunCoreAsync(sp, _folder, dryRun: true, requireEmptyDatabase: false, restartNumbers: false, TextWriter.Null,
                TestContext.Current.CancellationToken));

            Assert.True(result.DryRun);
            Assert.Empty(result.Problems);
            Assert.Equal(3, result.Tickets);
            Assert.Equal(before, await _world.Db(async db => (await db.Companies.CountAsync(), await db.Tickets.CountAsync(), await db.Users.CountAsync())));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_package_with_a_ticket_in_the_wrong_company_is_refused_before_anything_is_written()
    {
        WritePackage(t => t.Number.EndsWith("-2") ? t with { CompanyKey = "g:n" } : t); // the client of the second ticket is in the first company
        var before = await _world.Db(async db => (await db.Companies.CountAsync(), await db.Tickets.CountAsync(), await db.Users.CountAsync()));
        try
        {
            var result = await InScope(sp => LegacyImport.RunCoreAsync(sp, _folder, dryRun: false, requireEmptyDatabase: false, restartNumbers: false, TextWriter.Null,
                TestContext.Current.CancellationToken));

            Assert.Contains(result.Problems, p => p.Contains("not the company of its client"));
            Assert.Equal(0, result.Tickets);
            Assert.Equal(before, await _world.Db(async db => (await db.Companies.CountAsync(), await db.Tickets.CountAsync(), await db.Users.CountAsync())));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_import_refuses_a_database_that_is_not_empty_and_changes_nothing()
    {
        WritePackage();
        var before = await _world.Db(async db => (await db.Companies.CountAsync(), await db.Tickets.CountAsync()));
        try
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(sp => LegacyImport.RunCoreAsync(sp, _folder, dryRun: false, requireEmptyDatabase: true,
                restartNumbers: true, TextWriter.Null, TestContext.Current.CancellationToken)));

            Assert.Contains("The database is not empty", refused.Message);
            Assert.Equal(before, await _world.Db(async db => (await db.Companies.CountAsync(), await db.Tickets.CountAsync())));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Run_from_the_command_it_says_what_is_wrong_in_words_and_fails()
    {
        using var output = new StringWriter();

        var code = await InScope(sp => LegacyImport.RunAsync(sp, Path.Combine(_folder, "missing"), dryRun: true, output, TestContext.Current.CancellationToken));

        Assert.Equal(1, code);
        Assert.Contains("does not exist", output.ToString());
        Assert.Equal(1, await InScope(sp => LegacyImport.RunAsync(sp, null, dryRun: true, output, TestContext.Current.CancellationToken)));
    }
}
