namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>One searchable chunk held in memory. <see cref="Unit"/> is the embedding scaled to length 1, so cosine
/// similarity with a (also normalised) query is just a dot product.</summary>
public sealed record VectorEntry(Guid ChunkId, Guid? TicketId, Guid? DocumentId, float[] Unit);

/// <summary>
/// An in-memory copy of every chunk that can appear in a search result. Before this existed, every question loaded
/// and JSON-parsed ~100 MB of vectors from SQL Server, twice. Entries are rebuilt after a short time-to-live and
/// whenever the indexer or document management changes the corpus.
/// </summary>
public interface IVectorIndex
{
    Task<IReadOnlyList<VectorEntry>> GetAsync(CancellationToken ct = default);

    /// <summary>Marks the cached copy stale; the next call to <see cref="GetAsync"/> reloads it.</summary>
    void Invalidate();

    /// <summary>Scales <paramref name="vector"/> to unit length (a zero vector is returned unchanged).</summary>
    static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector) sum += (double)v * v;
        if (sum == 0) return vector;

        var inv = (float)(1.0 / Math.Sqrt(sum));
        var unit = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++) unit[i] = vector[i] * inv;
        return unit;
    }

    /// <summary>Cosine similarity of two unit vectors (0 when the lengths differ, e.g. a different model's dimension).</summary>
    static float Dot(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0;
        for (var i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot;
    }
}
