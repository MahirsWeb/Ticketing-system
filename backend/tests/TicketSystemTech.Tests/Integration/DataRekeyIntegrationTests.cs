using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TicketSystemTech.Api.Startup;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>--rekey-data against the real (throwaway) database: what is stored ends up under the new key, and everything still reads.</summary>
[Collection(ApiCollection.Name)]
public class DataRekeyIntegrationTests
{
    private readonly ApiWorld _world;

    public DataRekeyIntegrationTests(ApiWorld world) => _world = world;

    [SqlFact]
    public async Task Everything_encrypted_at_rest_is_written_again_under_the_new_key_and_still_reads()
    {
        var person = await _world.CreateActorAsync("rekeyed", UserRole.Employee, _world.BranchA, null);
        var before = await _world.Db(db => DataRekey.KeysInUseAsync(db));
        Assert.NotEmpty(before); // there is something encrypted to begin with

        var result = await DataRekey.RunCoreAsync(_world.Factory.Services, new ConfigurationBuilder().Build(), rewrite: true, revokeOldKeys: false,
            allowUnprotected: true, allowRunning: true, TestContext.Current.CancellationToken);

        Assert.True(result.Values > 0);
        Assert.Equal(before.Values.Sum(), result.KeysBefore.Values.Sum());
        var after = Assert.Single(result.KeysAfter); // all of it, under one key ...
        Assert.Equal(result.KeyKept, after.Key);    // ... the new one
        Assert.DoesNotContain(result.KeyKept, before.Keys);

        // The stored values really are under the new key (read again, not trusted from the result), and the application reads them:
        Assert.Equal(new[] { result.KeyKept }, (await _world.Db(db => DataRekey.KeysInUseAsync(db))).Keys);
        var login = await _world.Send(null, HttpMethod.Post, "/api/auth/login", new { email = person.Email, password = ApiWorld.Password });
        Assert.Equal(200, login.Code);
        Assert.Equal(person.Email, (await _world.Db(db => db.Users.AsNoTracking().Where(u => u.Id == person.Id).Select(u => u.Email).SingleAsync())));

        // Nothing was retired without being asked, and the missing certificate was said out loud.
        Assert.Equal(0, result.Revoked);
        Assert.Contains(result.Warnings, w => w.Contains("no certificate"));
    }

    [SqlFact]
    public async Task Without_a_certificate_the_tool_refuses_unless_told_to_go_on_and_changes_nothing()
    {
        var before = await _world.Db(db => DataRekey.KeysInUseAsync(db));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => DataRekey.RunCoreAsync(_world.Factory.Services, new ConfigurationBuilder().Build(),
            rewrite: true, revokeOldKeys: false, allowUnprotected: false, allowRunning: true, TestContext.Current.CancellationToken));

        Assert.Contains("no certificate", refused.Message);
        Assert.Equal(before, await _world.Db(db => DataRekey.KeysInUseAsync(db))); // nothing was rewritten
    }

    [SqlFact]
    public async Task Asking_for_nothing_is_an_error_and_a_failure_is_reported_as_text_not_as_a_crash()
    {
        using var output = new StringWriter();

        var code = await DataRekey.RunAsync(_world.Factory.Services, new ConfigurationBuilder().Build(), rewrite: false, revokeOldKeys: false,
            allowUnprotected: false, allowRunning: false, output, TestContext.Current.CancellationToken);

        Assert.Equal(1, code);
        Assert.Contains("Nothing to do", output.ToString());
    }

    [SqlFact]
    public async Task While_the_application_runs_the_tool_refuses_even_though_no_connection_is_open_and_changes_nothing()
    {
        // The test host IS a running application: it holds the marker on this database. Give it a moment to take it, as a real start would.
        for (var i = 0; i < 50 && !await MarkerIsHeldAsync(); i++) await Task.Delay(100, TestContext.Current.CancellationToken);
        var before = await _world.Db(db => DataRekey.KeysInUseAsync(db));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => DataRekey.RunCoreAsync(_world.Factory.Services, new ConfigurationBuilder().Build(),
            rewrite: true, revokeOldKeys: false, allowUnprotected: true, allowRunning: false, TestContext.Current.CancellationToken));

        Assert.Contains("application is running", refused.Message);
        Assert.Equal(before, await _world.Db(db => DataRekey.KeysInUseAsync(db))); // nothing was rewritten
    }

    private async Task<bool> MarkerIsHeldAsync() =>
        // An exclusive request from a connection of our own is refused (-1) exactly while a shared one is held.
        await _world.Db(async db =>
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'TicketSystemTech.InstanceRunning', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0; SELECT @r;";
            return Convert.ToInt32(await command.ExecuteScalarAsync()) < 0;
        });
}
