using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TicketSystemTech.Api.Startup;

namespace TicketSystemTech.Tests;

/// <summary>
/// TS-14.2: every start used to apply migrations, the data conversions and the seed before it served a request, wherever it ran. Now that is what a person
/// developing gets; a server brings the database up to date as a step of its own (<c>--migrate-only</c>) and the application only checks it, unless it is
/// told otherwise by name.
/// </summary>
public class StartupMigrateDefaultTests
{
    private sealed class Env : IHostEnvironment
    {
        public Env(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string? migrate = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(migrate is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["Startup:Migrate"] = migrate }).Build();

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void Unasked_it_migrates_where_a_person_is_developing_and_nowhere_else(string environment, bool expected) =>
        Assert.Equal(expected, StartupTasks.MigratesOnStart(Config(), new Env(environment)));

    [Theory]
    [InlineData("Production", "true", true)]
    [InlineData("Development", "false", false)]
    public void Asked_for_by_name_it_is_what_was_asked(string environment, string asked, bool expected) =>
        Assert.Equal(expected, StartupTasks.MigratesOnStart(Config(asked), new Env(environment)));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_migrate_only_step_always_migrates_it_is_what_it_is_for(string environment) =>
        Assert.True(StartupTasks.MigratesOnStart(Config("false"), new Env(environment), migrateOnlyStep: true));
}
