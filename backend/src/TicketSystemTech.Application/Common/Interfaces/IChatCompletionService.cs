namespace TicketSystemTech.Application.Common.Interfaces;

public interface IChatCompletionService
{
    /// <summary>
    /// Generates an answer strictly grounded in the supplied context. Returns null when no
    /// provider is configured (e.g. Google AI key not set) so callers can fall back gracefully.
    /// </summary>
    Task<string?> AskAsync(string systemInstruction, string context, string question, CancellationToken ct = default);
}
