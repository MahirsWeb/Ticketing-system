namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Generates vector embeddings for semantic search. Returns null when no embedding provider is
/// configured yet (e.g. Google AI API key not set) — callers should fall back to keyword search.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
}
