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

/// <summary>Stands in for Brevo: sends nothing, remembers what it was asked to send, and answers as the test says.</summary>
public sealed class FakeEmailTransport : IEmailTransport
{
    private readonly object _gate = new();
    private readonly List<(string To, string Subject)> _calls = new();

    /// <summary>What to answer for a given recipient. Throwing here stands for a transport that blows up.</summary>
    public Func<string, EmailSendResult> Answer { get; set; } = _ => EmailSendResult.Sent();

    public IReadOnlyList<(string To, string Subject)> Calls
    {
        get { lock (_gate) return _calls.ToList(); }
    }

    public void Reset()
    {
        lock (_gate) _calls.Clear();
        Answer = _ => EmailSendResult.Sent();
    }

    public Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        lock (_gate) _calls.Add((toEmail, subject));
        return Task.FromResult(Answer(toEmail));
    }
}
