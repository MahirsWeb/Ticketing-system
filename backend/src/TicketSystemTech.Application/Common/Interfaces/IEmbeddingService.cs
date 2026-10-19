namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Generates vector embeddings for semantic search. Returns null when no embedding provider is
/// configured yet (e.g. Google AI API key not set) — callers should fall back to keyword search.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Identifies the model that produced the vectors. It is stored next to every vector: vectors from
    /// different models are not comparable, so a future model change must not silently mix with old data.</summary>
    string ModelName { get; }

    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
}
