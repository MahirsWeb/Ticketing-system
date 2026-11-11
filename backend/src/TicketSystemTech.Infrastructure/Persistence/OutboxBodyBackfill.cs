using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Persistence;

/// <summary>
/// Outbox rows written before the body was encrypted still hold it as plain text (a reset link with its token, a temporary password) for the
/// 7 or 30 days they are kept. At start-up each of them is dealt with once: a message that was already delivered, skipped or given up on has
/// no use for its text, so the text is dropped; one still waiting to go out is encrypted in place, so it is still sent. Rows that are already
/// ciphertext (or empty) are not touched, so running this again changes nothing.
/// </summary>
public static class OutboxBodyBackfill
{
    // Status 0 is EmailOutboxStatus.Pending: waiting to be sent. Written as literals (not built from text) so that the queries are plainly constant.
    public static async Task<(int Dropped, int Encrypted)> RunAsync(AppDbContext db, IPiiProtector piiProtector, ILogger logger, CancellationToken ct = default)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var ownsConnection = connection.State != System.Data.ConnectionState.Open;
        if (ownsConnection) await connection.OpenAsync(ct);

        try
        {
            int dropped;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "UPDATE [EmailOutbox] SET [HtmlBody] = N'' WHERE [Status] <> 0 AND [HtmlBody] IS NOT NULL AND [HtmlBody] <> N'' AND [HtmlBody] NOT LIKE N'CfDJ8%'";
                dropped = await cmd.ExecuteNonQueryAsync(ct);
            }

            var waiting = new List<(Guid Id, string Body)>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT [Id], [HtmlBody] FROM [EmailOutbox] WHERE [Status] = 0 AND [HtmlBody] IS NOT NULL AND [HtmlBody] <> N'' AND [HtmlBody] NOT LIKE N'CfDJ8%'";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) waiting.Add((reader.GetGuid(0), reader.GetString(1)));
            }

            foreach (var (id, body) in waiting)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE [EmailOutbox] SET [HtmlBody] = @body WHERE [Id] = @id";
                cmd.Parameters.AddWithValue("@body", piiProtector.Encrypt(body)!);
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            if (dropped + waiting.Count > 0)
                logger.LogInformation("Outbox bodies written before encryption: {Dropped} of finished messages dropped, {Encrypted} of waiting messages encrypted", dropped, waiting.Count);
            return (dropped, waiting.Count);
        }
        finally
        {
            if (ownsConnection) await connection.CloseAsync();
        }
    }
}
