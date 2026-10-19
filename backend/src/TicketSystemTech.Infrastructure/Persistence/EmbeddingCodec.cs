using System.Text.Json;

namespace TicketSystemTech.Infrastructure.Persistence;

/// <summary>
/// Vectors are stored as the raw little-endian bytes of their float32 values (varbinary). The previous format, a
/// JSON array in an nvarchar column, spent ~39,000 characters (78 KB, UTF-16) on a 3072-number vector that needs
/// 12,288 bytes, and had to be parsed number by number on every search. Both formats carry exactly the same float32
/// values, so converting between them is lossless.
/// </summary>
public static class EmbeddingCodec
{
    public static byte[] Encode(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] Decode(byte[] bytes)
    {
        if (bytes.Length % sizeof(float) != 0)
            throw new InvalidDataException($"An embedding must be a whole number of 4-byte floats, got {bytes.Length} bytes.");

        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    /// <summary>Reads the legacy JSON representation (used only by the one-time conversion of existing rows).</summary>
    public static float[]? FromLegacyJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<float[]>(json);
}
