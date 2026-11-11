using Microsoft.EntityFrameworkCore;
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

        var result = await DataRekey.RekeyAsync(_world.Factory.Services, revokeOldKeys: false);

        Assert.True(result.Values > 0);
        Assert.Equal(before.Values.Sum(), result.KeysBefore.Values.Sum());
        var after = Assert.Single(result.KeysAfter); // all of it, under one key ...
        Assert.Equal(result.NewKeyId, after.Key);    // ... the new one
        Assert.DoesNotContain(result.NewKeyId, before.Keys);

        // The stored values really are under the new key (read again, not trusted from the result), and the application reads them:
        Assert.Equal(new[] { result.NewKeyId }, (await _world.Db(db => DataRekey.KeysInUseAsync(db))).Keys);
        var login = await _world.Send(null, HttpMethod.Post, "/api/auth/login", new { email = person.Email, password = ApiWorld.Password });
        Assert.Equal(200, login.Code);
        Assert.Equal(person.Email, (await _world.Db(db => db.Users.AsNoTracking().Where(u => u.Id == person.Id).Select(u => u.Email).SingleAsync())));

        // Nothing was retired without being asked.
        Assert.Equal(0, result.Revoked);
    }
}
