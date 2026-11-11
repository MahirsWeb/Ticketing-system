using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Api.Startup;

namespace TicketSystemTech.Tests;

/// <summary>
/// --rekey-data: the part that does not need a database. A real key ring in a temporary folder, a value written under the old key, a new
/// key, the old key retired. (That the stored data really is written again under the new key is in DataRekeyIntegrationTests.)
/// </summary>
public class DataRekeyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rekey-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;

    public DataRekeyTests()
    {
        _services = new ServiceCollection()
            .AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_folder)).Services
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private IDataProtector Protector => _services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
    private IKeyManager Keys => _services.GetRequiredService<IKeyManager>();

    [Fact]
    public void The_key_a_value_is_under_can_be_read_off_it_without_decrypting()
    {
        var value = Protector.Protect("secret");

        var id = DataRekey.KeyIdOf(value);

        Assert.NotNull(id);
        Assert.Contains(Keys.GetAllKeys(), k => k.KeyId == id);
        Assert.Null(DataRekey.KeyIdOf("plain text, never encrypted"));
        Assert.Null(DataRekey.KeyIdOf(null));
        Assert.Null(DataRekey.KeyIdOf("CfDJ8-not-really-base64-@@@"));
    }

    [Fact]
    public async Task A_new_key_dated_now_is_the_one_used_from_then_on_and_the_old_one_can_be_retired()
    {
        var old = Protector.Protect("written long ago");
        var oldKey = DataRekey.KeyIdOf(old)!.Value;

        var newKey = DataRekey.CreateKeyInForce(Keys, TimeProvider.System);
        await DataRekey.WaitUntilInForceAsync(_services.GetRequiredService<IDataProtectionProvider>(), newKey.KeyId, TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);

        var fresh = Protector.Protect("written now");
        Assert.Equal(newKey.KeyId, DataRekey.KeyIdOf(fresh)); // from now on, the new key (this is what the tool relies on)
        Assert.Equal("written long ago", Protector.Unprotect(old)); // and until the old one is retired it still opens its own

        var revoked = DataRekey.RevokeOtherKeys(Keys, newKey.KeyId);

        Assert.Equal(1, revoked);
        Assert.Contains(Keys.GetAllKeys(), k => k.KeyId == oldKey && k.IsRevoked);
        Assert.DoesNotContain(Keys.GetAllKeys(), k => k.KeyId == newKey.KeyId && k.IsRevoked);
        // The retired key opens nothing any more. (Data Protection notices a moment after, like it does for a new key.)
        var refused = false;
        for (var i = 0; i < 50 && !refused; i++)
        {
            try { Protector.Unprotect(old); await Task.Delay(100, TestContext.Current.CancellationToken); }
            catch (CryptographicException) { refused = true; }
        }
        Assert.True(refused, "a value under a retired key was still readable after 5 seconds");
        Assert.Equal("written now", Protector.Unprotect(fresh));
    }

    [Fact]
    public void Retiring_twice_retires_nothing_more()
    {
        var old = Protector.Protect("x");
        var newKey = DataRekey.CreateKeyInForce(Keys, TimeProvider.System);
        _ = old;

        Assert.Equal(1, DataRekey.RevokeOtherKeys(Keys, newKey.KeyId));
        Assert.Equal(0, DataRekey.RevokeOtherKeys(Keys, newKey.KeyId));
    }

    [Fact]
    public void Nothing_is_retired_while_any_value_is_still_under_another_key()
    {
        var keep = Guid.NewGuid();
        var stray = Guid.NewGuid();

        DataRekey.EnsureNothingIsLeftUnderOtherKeys(new Dictionary<Guid, int> { [keep] = 12 }, keep); // all of it under the new key: fine
        DataRekey.EnsureNothingIsLeftUnderOtherKeys(new Dictionary<Guid, int>(), keep);               // nothing encrypted at all: fine

        var refused = Assert.Throws<InvalidOperationException>(() =>
            DataRekey.EnsureNothingIsLeftUnderOtherKeys(new Dictionary<Guid, int> { [keep] = 12, [stray] = 2 }, keep));
        Assert.Contains("2 value(s)", refused.Message);
        Assert.Contains(stray.ToString(), refused.Message);
    }
}
