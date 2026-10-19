using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Tests.Integration;

/// <summary>Stands in for the Google embedding API. With <see cref="Enabled"/> off it behaves like an installation without an
/// AI key (returns null), which sends the knowledge base down its keyword path instead of the vector path.</summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    public const string Model = "authz-test-model";

    public string ModelName => Model;

    public bool Enabled { get; set; } = true;

    public Task<float[]?> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<float[]?>(Enabled ? new[] { 1f, 0f, 0f } : null);
}

/// <summary>Stands in for Gemini and remembers the context it was handed, i.e. exactly the text a model would have been shown.</summary>
public sealed class FakeChatCompletionService : IChatCompletionService
{
    public string? LastContext { get; private set; }

    public Task<string?> AskAsync(string systemInstruction, string context, string question, CancellationToken ct = default)
    {
        LastContext = context;
        return Task.FromResult<string?>("test answer");
    }
}

/// <summary>Never sends anything; remembers what would have been.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public List<(string To, string Subject)> Sent { get; } = new();

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        lock (Sent) Sent.Add((toEmail, subject));
        return Task.CompletedTask;
    }
}
