namespace TicketSystemTech.Application.KnowledgeBase;

public enum ReindexState
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

/// <param name="Total">Tickets that needed embedding when the run started.</param>
/// <param name="Processed">Tickets attempted so far.</param>
/// <param name="Embedded">Of those, how many now have an embedding.</param>
/// <param name="Failed">Of those, how many the embedding provider returned nothing for (rate limit, no key, error).</param>
public sealed record ReindexStatus(
    ReindexState State,
    int Total,
    int Processed,
    int Embedded,
    int Failed,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    string? Message);

/// <summary>
/// Rebuilds missing knowledge-base embeddings in the background. The old endpoint did the whole run inside one HTTP
/// request, which for a large corpus meant hours, a request timeout somewhere in the middle, and no way to see progress.
/// </summary>
public interface IKnowledgeBaseReindexJob
{
    ReindexStatus GetStatus();

    /// <summary>Queues a run over every ticket that still needs an embedding. False when one is already running (there is only ever one).</summary>
    bool TryStart();

    /// <summary>Queues the rebuild of one uploaded document. Shares the single-run rule with <see cref="TryStart"/>.</summary>
    bool TryStartDocument(Guid documentId);
}
