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
/// Idempotent: a row is only touched if IPiiProtector.Decrypt(rawColumnValue) returns the value unchanged
/// (i.e. it isn't valid ciphertext yet — see PiiProtector's plaintext fallback). Already-migrated rows are
/// skipped, so this is safe to leave running on every startup, the same way EF's own MigrateAsync() is.
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
            await BackfillUsersAsync(connection, piiProtector, logger, ct);
            await BackfillCompanyAddressesAsync(connection, piiProtector, logger, ct);
        }
        finally
        {
            if (ownsConnection) await connection.CloseAsync();
        }
    }

    private static async Task BackfillUsersAsync(SqlConnection connection, IPiiProtector piiProtector, ILogger logger, CancellationToken ct)
    {
        var toMigrate = new List<(Guid Id, string? Email, string? UserName, string? PhoneNumber)>();

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

                var stillPlaintext = email is not null && piiProtector.Decrypt(email) == email;
                if (stillPlaintext)
                {
                    toMigrate.Add((id, email, userName, phone));
                }
            }
        }

        if (toMigrate.Count == 0)
        {
            logger.LogInformation("PII backfill: AspNetUsers already fully encrypted, nothing to do.");
            return;
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
    }

    private static async Task BackfillCompanyAddressesAsync(SqlConnection connection, IPiiProtector piiProtector, ILogger logger, CancellationToken ct)
    {
        var toMigrate = new List<(Guid Id, string Address)>();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT [Id], [Address] FROM [Companies] WHERE [Address] IS NOT NULL";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var address = reader.GetString(1);
                if (piiProtector.Decrypt(address) == address)
                {
                    toMigrate.Add((id, address));
                }
            }
        }

        if (toMigrate.Count == 0)
        {
            logger.LogInformation("PII backfill: Companies.Address already fully encrypted, nothing to do.");
            return;
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
    }
}
