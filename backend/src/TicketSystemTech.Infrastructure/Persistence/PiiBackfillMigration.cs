using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Persistence;

/// <summary>
/// One-time (but safely repeatable) data backfill that encrypts legacy plaintext PII columns after the
/// AppDbContext value converters for Email/UserName/PhoneNumber/Address were introduced. Runs via raw SQL,
/// bypassing EF's converters entirely, so there's no ambiguity about double-encrypting a value.
///
/// Idempotent: a row is only touched if it is not ciphertext yet (IPiiProtector.IsProtected is false), so this is
/// safe to leave running on every startup, the same way EF's own MigrateAsync() is.
///
/// It also doubles as a key-ring canary: every row that IS ciphertext must still decrypt. If it does not (the
/// DataProtectionKeys table was lost, restored from another environment, or the key ring changed), startup is
/// aborted with a clear message instead of letting the app run and overwrite undecryptable values with garbage.
/// </summary>
public static class PiiBackfillMigration
{
    public static async Task RunAsync(AppDbContext db, IPiiProtector piiProtector, ILogger logger, CancellationToken ct = default)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var ownsConnection = connection.State != System.Data.ConnectionState.Open;
        if (ownsConnection) await connection.OpenAsync(ct);

        try
        {
            var undecryptable = await BackfillUsersAsync(connection, piiProtector, logger, ct);
            undecryptable += await BackfillCompanyAddressesAsync(connection, piiProtector, logger, ct);

            if (undecryptable > 0)
                throw new InvalidOperationException(
                    $"{undecryptable} encrypted value(s) cannot be decrypted with the current Data Protection key ring. " +
                    "The DataProtectionKeys table is missing, was replaced, or does not belong to this database. " +
                    "Restore the matching DataProtectionKeys rows before starting the application; " +
                    "continuing would corrupt personal data.");
        }
        finally
        {
            if (ownsConnection) await connection.CloseAsync();
        }
    }

    private static async Task<int> BackfillUsersAsync(SqlConnection connection, IPiiProtector piiProtector, ILogger logger, CancellationToken ct)
    {
        var toMigrate = new List<(Guid Id, string? Email, string? UserName, string? PhoneNumber)>();
        var undecryptable = 0;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT [Id], [Email], [UserName], [PhoneNumber] FROM [AspNetUsers]";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var email = reader.IsDBNull(1) ? null : reader.GetString(1);
                var userName = reader.IsDBNull(2) ? null : reader.GetString(2);
                var phone = reader.IsDBNull(3) ? null : reader.GetString(3);

                if (email is null) continue;

                if (!piiProtector.IsProtected(email))
                {
                    toMigrate.Add((id, email, userName, phone));
                }
                else if (!CanDecrypt(piiProtector, email))
                {
                    undecryptable++;
                    logger.LogError("PII backfill: AspNetUsers row {UserId} holds ciphertext that the current key ring cannot decrypt.", id);
                }
            }
        }

        if (toMigrate.Count == 0)
        {
            logger.LogInformation("PII backfill: AspNetUsers already fully encrypted, nothing to do.");
            return undecryptable;
        }

        logger.LogWarning("PII backfill: encrypting Email/UserName/PhoneNumber for {Count} AspNetUsers row(s)...", toMigrate.Count);
        var failures = 0;
        foreach (var u in toMigrate)
        {
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE [AspNetUsers] SET [Email] = @email, [UserName] = @userName, [PhoneNumber] = @phone, " +
                                   "[NormalizedEmail] = @normEmail, [NormalizedUserName] = @normUserName WHERE [Id] = @id";
                cmd.Parameters.AddWithValue("@email", (object?)piiProtector.Encrypt(u.Email) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@userName", (object?)piiProtector.Encrypt(u.UserName) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@phone", (object?)piiProtector.Encrypt(u.PhoneNumber) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@normEmail", (object?)piiProtector.Hash(u.Email) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@normUserName", (object?)piiProtector.Hash(u.UserName) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", u.Id);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex)
            {
                // Don't let one bad row (e.g. a pre-existing duplicate Email/UserName that only collides
                // once normalized) take down the whole app on startup — log it for manual follow-up and
                // keep going. The row stays plaintext and gets retried on the next startup.
                failures++;
                logger.LogError(ex, "PII backfill: failed to encrypt AspNetUsers row {UserId} — left as plaintext for manual review.", u.Id);
            }
        }
        logger.LogWarning("PII backfill: AspNetUsers encryption complete ({Count} row(s), {Failures} failed).", toMigrate.Count - failures, failures);
        return undecryptable;
    }

    private static bool CanDecrypt(IPiiProtector piiProtector, string value)
    {
        try
        {
            piiProtector.Decrypt(value);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static async Task<int> BackfillCompanyAddressesAsync(SqlConnection connection, IPiiProtector piiProtector, ILogger logger, CancellationToken ct)
    {
        var toMigrate = new List<(Guid Id, string Address)>();
        var undecryptable = 0;

        await using (var cmd = connection.CreateCommand())
        {
            // Empty strings are skipped: Encrypt("") returns "", so they would look "unmigrated" and be rewritten forever.
            cmd.CommandText = "SELECT [Id], [Address] FROM [Companies] WHERE [Address] IS NOT NULL AND [Address] <> ''";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var address = reader.GetString(1);
                if (!piiProtector.IsProtected(address))
                {
                    toMigrate.Add((id, address));
                }
                else if (!CanDecrypt(piiProtector, address))
                {
                    undecryptable++;
                    logger.LogError("PII backfill: Companies row {CompanyId} holds an Address that the current key ring cannot decrypt.", id);
                }
            }
        }

        if (toMigrate.Count == 0)
        {
            logger.LogInformation("PII backfill: Companies.Address already fully encrypted, nothing to do.");
            return undecryptable;
        }

        logger.LogWarning("PII backfill: encrypting Address for {Count} Companies row(s)...", toMigrate.Count);
        var failures = 0;
        foreach (var c in toMigrate)
        {
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE [Companies] SET [Address] = @address WHERE [Id] = @id";
                cmd.Parameters.AddWithValue("@address", piiProtector.Encrypt(c.Address)!);
                cmd.Parameters.AddWithValue("@id", c.Id);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex)
            {
                failures++;
                logger.LogError(ex, "PII backfill: failed to encrypt Companies.Address row {CompanyId} — left as plaintext for manual review.", c.Id);
            }
        }
        logger.LogWarning("PII backfill: Companies.Address encryption complete ({Count} row(s), {Failures} failed).", toMigrate.Count - failures, failures);
        return undecryptable;
    }
}
