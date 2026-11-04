namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>Who is waiting for an embedding. It decides how patient the service is with a provider that says "slow down".</summary>
public enum EmbeddingPriority
{
    /// <summary>A person is waiting for an answer (a search, a question): no deliberate delay, at most a short retry.</summary>
    Interactive,

    /// <summary>Indexing work nobody is waiting for: spaced out and patient, so a bulk run gets through a tight quota
    /// without ever holding up an interactive call.</summary>
    Background
}

/// <summary>
/// Generates vector embeddings for semantic search. Returns null when no embedding provider is
/// configured (AI switched off, or no key / endpoint yet) — callers should fall back to keyword search.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Identifies the model that produced the vectors. It is stored next to every vector: vectors from
    /// different models are not comparable, so a model change must not silently mix with old data.</summary>
    string ModelName { get; }

    /// <summary>How many numbers a vector from the current setup has, when that is known without asking the provider.
    /// A stored vector of another length is left over from a different model.</summary>
    int? Dimensions { get; }

    /// <summary>False when AI is switched off or not configured: every call would return null without trying.</summary>
    bool IsAvailable { get; }

    Task<float[]?> EmbedAsync(string text, EmbeddingPriority priority, CancellationToken ct = default);

    /// <summary>Embeds for a person who is waiting.</summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default) => EmbedAsync(text, EmbeddingPriority.Interactive, ct);
}
