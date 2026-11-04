namespace TicketSystemTech.Application.Common.Interfaces;

public interface IChatCompletionService
{
    /// <summary>False when AI is switched off or not configured: <see cref="AskAsync"/> would return null without trying.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Generates an answer strictly grounded in the supplied context. Returns null when no
    /// provider is configured (AI switched off, or no key / endpoint yet) so callers can fall back gracefully.
    /// </summary>
    Task<string?> AskAsync(string systemInstruction, string context, string question, CancellationToken ct = default);
}
