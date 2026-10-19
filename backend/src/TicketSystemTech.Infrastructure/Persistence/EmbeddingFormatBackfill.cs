using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TicketSystemTech.Infrastructure.Persistence;

/// <summary>
/// One-time, resumable conversion of existing vectors from the old JSON text column (EmbeddingJson) to the binary
/// column the model now reads (EmbeddingBinary). It runs through raw SQL, bypassing the EF converters, in keyset
/// batches so memory stays small and a stop half way simply continues next start.
///
/// The old column is left untouched on purpose: it is the only copy of vectors that took hours of paid API calls to
/// create. Once search has been seen working, clear it to reclaim the space:
///   UPDATE KnowledgeBaseChunks SET EmbeddingJson = NULL WHERE EmbeddingBinary IS NOT NULL;
/// (and a later migration can drop the column).
/// </summary>
public static class EmbeddingFormatBackfill
{
    private const int BatchSize = 200;

    /// <param name="legacyModelName">Model recorded for converted rows. They were all produced by the model that
    /// was hard-wired before model names were stored.</param>
    public static async Task RunAsync(AppDbContext db, string legacyModelName, ILogger logger, CancellationToken ct = default)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var ownsConnection = connection.State != System.Data.ConnectionState.Open;
        if (ownsConnection) await connection.OpenAsync(ct);

        try
        {
            if (!await LegacyColumnExistsAsync(connection, ct)) return;

            var remaining = await CountAsync(connection,
                "SELECT COUNT(*) FROM [KnowledgeBaseChunks] WHERE [EmbeddingBinary] IS NULL AND [EmbeddingJson] IS NOT NULL", ct);
            if (remaining == 0) return;

            logger.LogWarning("Embedding backfill: converting {Count} vector(s) from JSON to binary (one-time)...", remaining);

            var converted = 0;
            var failed = 0;
            var lastId = Guid.Empty;
            while (true)
            {
                var batch = new List<(Guid Id, string Json)>();
                await using (var read = connection.CreateCommand())
                {
                    read.CommandText =
                        $"SELECT TOP ({BatchSize}) [Id], [EmbeddingJson] FROM [KnowledgeBaseChunks] " +
                        "WHERE [Id] > @last AND [EmbeddingBinary] IS NULL AND [EmbeddingJson] IS NOT NULL ORDER BY [Id]";
                    read.Parameters.AddWithValue("@last", lastId);
                    await using var reader = await read.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        batch.Add((reader.GetGuid(0), reader.GetString(1)));
                }

                if (batch.Count == 0) break;

                foreach (var (id, json) in batch)
                {
                    lastId = id;
                    try
                    {
                        var vector = EmbeddingCodec.FromLegacyJson(json);
                        if (vector is null || vector.Length == 0) { failed++; continue; }

                        await using var write = connection.CreateCommand();
                        write.CommandText =
                            "UPDATE [KnowledgeBaseChunks] SET [EmbeddingBinary] = @bytes, [EmbeddingModel] = COALESCE([EmbeddingModel], @model) WHERE [Id] = @id";
                        write.Parameters.Add("@bytes", System.Data.SqlDbType.VarBinary, -1).Value = EmbeddingCodec.Encode(vector);
                        write.Parameters.AddWithValue("@model", legacyModelName);
                        write.Parameters.AddWithValue("@id", id);
                        await write.ExecuteNonQueryAsync(ct);
                        converted++;
                    }
                    catch (Exception ex) when (ex is System.Text.Json.JsonException or SqlException)
                    {
                        // One unreadable row must not stop the app from starting; it keeps its JSON and is reported.
                        failed++;
                        logger.LogError(ex, "Embedding backfill: chunk {ChunkId} could not be converted.", id);
                    }
                }

                if ((converted + failed) % 2000 < BatchSize)
                    logger.LogInformation("Embedding backfill: {Done}/{Total} processed", converted + failed, remaining);
            }

            logger.LogWarning("Embedding backfill finished: {Converted} converted, {Failed} failed.", converted, failed);
        }
        finally
        {
            if (ownsConnection) await connection.CloseAsync();
        }
    }

    private static async Task<bool> LegacyColumnExistsAsync(SqlConnection connection, CancellationToken ct) =>
        await CountAsync(connection,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[KnowledgeBaseChunks]') AND name = N'EmbeddingJson'", ct) > 0;

    private static async Task<int> CountAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }
}
