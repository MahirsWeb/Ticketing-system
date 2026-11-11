using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Startup;

/// <summary>
/// Two commands, so that each does one thing and can be repeated safely.
/// <c>dotnet TicketSystemTech.Api.dll --rekey-data</c> makes a new key and writes everything that is encrypted at rest again under it (users'
/// e-mail address, user name and telephone, company addresses, the outbox, the saved mailbox tokens), then checks that nothing is left under any
/// other key. <c>--revoke-old-keys</c> by itself retires every key but the newest, after checking that nothing is under them; with both flags the
/// second follows the first. Data Protection makes a new key now and then but keeps the old ones, because what was written under them can only
/// be read with them, so a key ring that was ever unprotected keeps handing out its old keys to anyone with a copy of the database until this is run.
/// It refuses to run without a key ring certificate (a new key stored in the clear protects nothing; <c>--allow-unprotected-keys</c> overrides, with a
/// warning), while a key that is already scheduled to start later exists (automatic rotation), or while other programs are connected to the database
/// (the application must be stopped: <c>--allow-running</c> overrides). Take a backup first: revoking is not undone, and links already sent for a
/// password reset or an e-mail address stop working because they were made under the old keys.
/// </summary>
public static class DataRekey
{
    /// <summary>What a run did: which keys the stored values were under before and after, how many old keys were retired, and what to be warned of.</summary>
    public sealed record Result(Guid KeyKept, int Values, IReadOnlyDictionary<Guid, int> KeysBefore, IReadOnlyDictionary<Guid, int> KeysAfter, int Revoked, IReadOnlyList<string> Warnings);

    private const string GraphTokenPurpose = "EmailConnection.OAuthTokens";

    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration, bool rewrite, bool revokeOldKeys, bool allowUnprotected, bool allowRunning,
        TextWriter output, CancellationToken ct = default)
    {
        try
        {
            var result = await RunCoreAsync(services, configuration, rewrite, revokeOldKeys, allowUnprotected, allowRunning, ct);
            foreach (var warning in result.Warnings) await output.WriteLineAsync("WARNING: " + warning);
            if (rewrite) await output.WriteLineAsync($"Rewrote {result.Values} encrypted value(s) under the key {result.KeyKept}.");
            await output.WriteLineAsync("Before: " + Describe(result.KeysBefore));
            await output.WriteLineAsync("After:  " + Describe(result.KeysAfter));
            await output.WriteLineAsync(revokeOldKeys
                ? $"Retired {result.Revoked} old key(s). They no longer open anything."
                : "The old keys are still in the key ring. When this has been checked, run again with --revoke-old-keys (alone) to retire them.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException or Microsoft.Data.SqlClient.SqlException or IOException)
        {
            await output.WriteLineAsync("Not done: " + ex.Message);
            return 1;
        }
    }

    private static string Describe(IReadOnlyDictionary<Guid, int> keys) =>
        keys.Count == 0 ? "(nothing encrypted)" : string.Join(", ", keys.Select(k => $"{k.Value} under {k.Key}"));

    public static async Task<Result> RunCoreAsync(IServiceProvider services, IConfiguration configuration, bool rewrite, bool revokeOldKeys, bool allowUnprotected, bool allowRunning, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<AppDbContext>();
        var keyManager = provider.GetRequiredService<IKeyManager>();
        var time = provider.GetRequiredService<TimeProvider>();
        var warnings = new List<string>();

        await EnsureNobodyElseIsConnectedAsync(db, allowRunning, ct);
        if (!rewrite && !revokeOldKeys) throw new InvalidOperationException("Nothing to do: give --rekey-data, --revoke-old-keys, or both.");

        var before = await KeysInUseAsync(db, ct);
        Guid keep;
        var values = 0;

        if (rewrite)
        {
            if (!Configuration.KeyRingProtection.IsConfigured(configuration))
            {
                if (!allowUnprotected)
                    throw new InvalidOperationException(
                        "The key ring has no certificate (DataProtection:CertificatePath or CertificateThumbprint): a new key would be stored in the database in the clear, " +
                        "and re-keying would protect nothing. Put the certificate in place first, or give --allow-unprotected-keys to do it anyway.");
                warnings.Add("the key ring has no certificate: the new key is stored in the database without encryption, so a copy of the database still opens everything.");
            }

            if (NextScheduledKey(keyManager, time) is { } future)
                throw new InvalidOperationException(
                    $"A key that starts at {future:u} already exists (Data Protection's own rotation made it). A key made now would start after it, and nothing would use it until then. " +
                    "Run this again after that moment.");

            var newKey = CreateKeyInForce(keyManager, time);
            await WaitUntilInForceAsync(provider.GetRequiredService<IDataProtectionProvider>(), newKey.KeyId, TimeSpan.FromSeconds(30), time, ct);
            values = await RewriteAsync(db, provider.GetRequiredService<IDataProtectionProvider>().CreateProtector(GraphTokenPurpose), ct);
            keep = newKey.KeyId;
        }
        else
        {
            keep = KeyInForce(keyManager, time);
        }

        var after = await KeysInUseAsync(db, ct);
        EnsureNothingIsLeftUnderOtherKeys(after, keep);

        var revoked = revokeOldKeys ? RevokeOtherKeys(keyManager, keep) : 0;
        return new Result(keep, values, before, after, revoked, warnings);
    }

    /// <summary>When a key that is not active yet starts (Data Protection makes the next one ahead of time), or null. A key made now would be dated after
    /// it and nothing would use it until then.</summary>
    public static DateTimeOffset? NextScheduledKey(IKeyManager keyManager, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var later = keyManager.GetAllKeys().Where(k => !k.IsRevoked && k.ActivationDate > now).Select(k => k.ActivationDate).ToList();
        return later.Count == 0 ? null : later.Max();
    }

    /// <summary>The key that protects new values now: the one activated most recently that is already active and not retired.</summary>
    public static Guid KeyInForce(IKeyManager keyManager, TimeProvider time) =>
        keyManager.GetAllKeys().Where(k => !k.IsRevoked && k.ActivationDate <= time.GetUtcNow()).OrderByDescending(k => k.ActivationDate).Select(k => k.KeyId).FirstOrDefault();

    /// <summary>Nothing may be writing while values are rewritten (it would write new ones under whichever key it saw). Other programs connected to this
    /// database are counted by the server (not the connections of this process); if the server does not allow the question, the check cannot be made.</summary>
    private static async Task EnsureNobodyElseIsConnectedAsync(AppDbContext db, bool allowRunning, CancellationToken ct)
    {
        if (allowRunning) return;
        int others;
        try
        {
            var pid = Environment.ProcessId;
            others = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.dm_exec_sessions WHERE is_user_process = 1 AND database_id = DB_ID() AND host_process_id <> {pid} AND program_name NOT LIKE N'Microsoft SQL Server Management Studio%'")
                .SingleAsync(ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            return; // the login may not look at other sessions (VIEW SERVER STATE); the operator was told to stop the application
        }

        if (others > 0)
            throw new InvalidOperationException(
                $"{others} other connection(s) to the database are open: the application is probably still running, and anything it writes meanwhile would go under the key it sees. " +
                "Stop it and run this again (or give --allow-running if those connections are not the application).");
    }

    /// <summary>Stops the run (before anything is retired) when any stored value is still under a key other than the new one.</summary>
    public static void EnsureNothingIsLeftUnderOtherKeys(IReadOnlyDictionary<Guid, int> keysInUse, Guid newKeyId)
    {
        var strays = keysInUse.Where(k => k.Key != newKeyId).ToList();
        if (strays.Count > 0)
            throw new InvalidOperationException(
                $"{strays.Sum(s => s.Value)} value(s) are still under other keys ({string.Join(", ", strays.Select(s => s.Key))}), so nothing was retired. " +
                "The application may be running and writing at the same time; stop it and run this again.");
    }

    /// <summary>
    /// A new key that is the one used from this moment. Data Protection protects with the key that was ACTIVATED most recently, so it is dated
    /// now, and never earlier than the newest key there is already (a key dated before an existing one would be made and then never chosen).
    /// </summary>
    public static IKey CreateKeyInForce(IKeyManager keyManager, TimeProvider time)
    {
        var activation = time.GetUtcNow();
        var newest = keyManager.GetAllKeys().Select(k => k.ActivationDate).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
        if (newest >= activation) activation = newest.AddTicks(1); // the smallest step: a key dated in the future is not used until then
        return keyManager.CreateNewKey(activation, activation.AddDays(90));
    }

    /// <summary>
    /// Data Protection takes a moment (about half a second in practice) to start protecting with a key that was just made. Values written
    /// before that would still go under the old key, so the rewrite waits until a probe really comes out under the new one.
    /// </summary>
    public static async Task WaitUntilInForceAsync(IDataProtectionProvider provider, Guid keyId, TimeSpan timeout, TimeProvider time, CancellationToken ct = default)
    {
        var probe = provider.CreateProtector("DataRekey.Probe");
        var until = time.GetUtcNow() + timeout;
        while (KeyIdOf(probe.Protect("probe")) != keyId)
        {
            if (time.GetUtcNow() > until)
                throw new InvalidOperationException($"The new key {keyId} was made but Data Protection is not using it after {timeout.TotalSeconds:0} seconds, so nothing was changed.");
            await Task.Delay(100, ct);
        }
    }

    /// <summary>Retires every key except the one that is kept. A retired key cannot open anything, so this is only called when nothing is under it.</summary>
    public static int RevokeOtherKeys(IKeyManager keyManager, Guid keep)
    {
        var revoked = 0;
        foreach (var key in keyManager.GetAllKeys().Where(k => k.KeyId != keep && !k.IsRevoked).ToList())
        {
            keyManager.RevokeKey(key.KeyId, "Replaced by --rekey-data: everything encrypted under it was written again under a newer key.");
            revoked++;
        }
        return revoked;
    }

    /// <summary>Writes every encrypted value again. The entity properties go through the encrypting converters, so marking them as
    /// changed is enough: what is saved is the same text under the key in force now.</summary>
    private static async Task<int> RewriteAsync(AppDbContext db, IDataProtector graphProtector, CancellationToken ct)
    {
        var count = 0;

        foreach (var chunk in (await db.Users.Select(u => u.Id).ToListAsync(ct)).Chunk(200))
        {
            foreach (var user in await db.Users.Where(u => chunk.Contains(u.Id)).ToListAsync(ct))
            {
                foreach (var name in new[] { nameof(user.Email), nameof(user.UserName), nameof(user.PhoneNumber) })
                {
                    db.Entry(user).Property(name).IsModified = true;
                    count++;
                }
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        foreach (var company in await db.Companies.ToListAsync(ct))
        {
            db.Entry(company).Property(nameof(company.Address)).IsModified = true;
            count++;
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        foreach (var chunk in (await db.EmailOutbox.Select(m => m.Id).ToListAsync(ct)).Chunk(200))
        {
            foreach (var item in await db.EmailOutbox.Where(m => chunk.Contains(m.Id)).ToListAsync(ct))
            {
                db.Entry(item).Property(nameof(item.ToEmail)).IsModified = true;
                db.Entry(item).Property(nameof(item.HtmlBody)).IsModified = true;
                count += 2;
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        // The saved mailbox tokens use a protector of their own (a different purpose), not the converters above.
        foreach (var connection in await db.EmailConnections.ToListAsync(ct))
        {
            connection.AccessToken = Reprotect(graphProtector, connection.AccessToken, ref count);
            connection.RefreshToken = Reprotect(graphProtector, connection.RefreshToken, ref count);
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return count;
    }

    private static string Reprotect(IDataProtector protector, string value, ref int count)
    {
        if (!IsProtected(value)) return value; // plaintext from before encryption existed: left for the application to deal with, as it always did
        count++;
        return protector.Protect(protector.Unprotect(value));
    }

    // ---------------------------------------------------------------- which key a stored value is under

    private const string CiphertextPrefix = "CfDJ8";

    private static bool IsProtected(string? value) => !string.IsNullOrEmpty(value) && value.StartsWith(CiphertextPrefix, StringComparison.Ordinal);

    /// <summary>A Data Protection payload starts with a 4-byte header and then the 16-byte id of the key it was made with.</summary>
    public static Guid? KeyIdOf(string? value)
    {
        if (!IsProtected(value)) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(value!);
            return bytes.Length >= 20 ? new Guid(bytes.AsSpan(4, 16)) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>How many stored values are under each key, read as they lie in the database (not decrypted).</summary>
    public static async Task<IReadOnlyDictionary<Guid, int>> KeysInUseAsync(AppDbContext db, CancellationToken ct = default)
    {
        var values = new List<string>();
        values.AddRange(await db.Database.SqlQuery<string>($"SELECT [Email] AS [Value] FROM [AspNetUsers] WHERE [Email] LIKE N'CfDJ8%'").ToListAsync(ct));
        values.AddRange(await db.Database.SqlQuery<string>($"SELECT [UserName] AS [Value] FROM [AspNetUsers] WHERE [UserName] LIKE N'CfDJ8%'").ToListAsync(ct));
        values.AddRange(await db.Database.SqlQuery<string>($"SELECT [PhoneNumber] AS [Value] FROM [AspNetUsers] WHERE [PhoneNumber] LIKE N'CfDJ8%'").ToListAsync(ct));
        values.AddRange(await db.Database.SqlQuery<string>($"SELECT [Address] AS [Value] FROM [Companies] WHERE [Address] LIKE N'CfDJ8%'").ToListAsync(ct));
        values.AddRange(await db.Database.SqlQuery<string>($"SELECT [ToEmail] AS [Value] FROM [EmailOutbox] WHERE [ToEmail] LIKE N'CfDJ8%'").ToListAsync(ct));
        values.AddRange(await db.Database.SqlQuery<string>($"SELECT [HtmlBody] AS [Value] FROM [EmailOutbox] WHERE [HtmlBody] LIKE N'CfDJ8%'").ToListAsync(ct));
        values.AddRange(await db.Database.SqlQuery<string>($"SELECT [AccessToken] AS [Value] FROM [EmailConnections] WHERE [AccessToken] LIKE N'CfDJ8%'").ToListAsync(ct));
        values.AddRange(await db.Database.SqlQuery<string>($"SELECT [RefreshToken] AS [Value] FROM [EmailConnections] WHERE [RefreshToken] LIKE N'CfDJ8%'").ToListAsync(ct));

        return values
            .Select(KeyIdOf)
            .Where(id => id is not null)
            .GroupBy(id => id!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
