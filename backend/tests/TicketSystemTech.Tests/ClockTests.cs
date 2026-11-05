using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Text.RegularExpressions;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;
using TicketSystemTech.Tests.Integration;

namespace TicketSystemTech.Tests;

/// <summary>
/// TS-15.2: the time comes from one clock (<see cref="TimeProvider"/>), so what depends on the time can be tested with a fixed one,
/// and nothing in the application reads the system clock by itself.
/// </summary>
public class ClockTests
{
    private static readonly DateTimeOffset Fixed = new(2031, 3, 14, 9, 26, 53, TimeSpan.Zero);

    [Fact]
    public void An_access_token_expires_the_configured_minutes_after_the_clock_of_the_application()
    {
        var clock = new FakeTimeProvider(Fixed);
        var tokens = new JwtTokenService(Options.Create(new JwtOptions { Secret = new string('k', 48), AccessTokenMinutes = 45 }), clock);

        var (_, expiresAt) = tokens.GenerateAccessToken(Guid.NewGuid(), "a@b.test", "A", "B", UserRole.Employee, null, null, "stamp");

        Assert.Equal(Fixed.UtcDateTime.AddMinutes(45), expiresAt);

        clock.Advance(TimeSpan.FromHours(2));
        var (_, later) = tokens.GenerateAccessToken(Guid.NewGuid(), "a@b.test", "A", "B", UserRole.Employee, null, null, "stamp");
        Assert.Equal(Fixed.UtcDateTime.AddHours(2).AddMinutes(45), later);
    }

    // The ratchet: nobody reads the system clock by hand again. The two places that may are the framework's own TimeProvider.System
    // (in Program.cs and in the command-line key tool) and the tests.
    [Fact]
    public void The_application_code_never_reads_the_system_clock_by_itself()
    {
        var source = SourceRoot();
        var offenders = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => Regex.IsMatch(x.Text, @"\b(DateTime|DateTimeOffset)\.(UtcNow|Now|Today)\b") && !x.Text.TrimStart().StartsWith("//") && !x.Text.TrimStart().StartsWith("///"))
            .Select(x => $"{Path.GetRelativePath(source, x.File)}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Use the TimeProvider instead of the system clock:\n" + string.Join("\n", offenders));
    }

    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "TicketSystemTech.Api");
            if (Directory.Exists(candidate)) return Path.Combine(dir.FullName, "src");
        }
        throw new DirectoryNotFoundException("The source folder (backend/src) was not found above " + AppContext.BaseDirectory);
    }
}

[Collection(ApiCollection.Name)]
public class CreationTimeTests
{
    private static readonly DateTimeOffset Fixed = new(2031, 3, 14, 9, 26, 53, TimeSpan.Zero);

    private readonly ApiWorld _world;

    public CreationTimeTests(ApiWorld world) => _world = world;

    [SqlFact]
    public async Task A_new_row_gets_its_creation_time_from_the_clock_of_the_application()
    {
        var clock = new FakeTimeProvider(Fixed);
        using var scope = _world.Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        var topic = new HelpTopic { Name = "clock " + Guid.NewGuid().ToString("N")[..6] };
        try
        {
            await using (var db = new AppDbContext(options, scope.ServiceProvider.GetRequiredService<IPiiProtector>(), null, clock))
            {
                db.HelpTopics.Add(topic);
                await db.SaveChangesAsync();
            }
            Assert.Equal(Fixed.UtcDateTime, topic.CreatedAt);

            // A time that was set on purpose is left alone, and a row that is only changed is not touched.
            var chosen = Fixed.UtcDateTime.AddDays(-3);
            var second = new HelpTopic { Name = topic.Name + "b", CreatedAt = chosen };
            await using (var db = new AppDbContext(options, scope.ServiceProvider.GetRequiredService<IPiiProtector>(), null, clock))
            {
                db.HelpTopics.Add(second);
                await db.SaveChangesAsync();
                clock.Advance(TimeSpan.FromDays(1));
                db.HelpTopics.Attach(topic).Entity.IsActive = false;
                await db.SaveChangesAsync();
            }
            Assert.Equal(chosen, second.CreatedAt);
            Assert.Equal(Fixed.UtcDateTime, topic.CreatedAt);
            await using var read = new AppDbContext(options, scope.ServiceProvider.GetRequiredService<IPiiProtector>());
            Assert.Equal(Fixed.UtcDateTime, await read.HelpTopics.Where(h => h.Id == topic.Id).Select(h => h.CreatedAt).SingleAsync());
        }
        finally
        {
            await using var cleanup = new AppDbContext(options, scope.ServiceProvider.GetRequiredService<IPiiProtector>());
            await cleanup.HelpTopics.Where(h => h.Name.StartsWith(topic.Name)).ExecuteDeleteAsync();
        }
    }
}
