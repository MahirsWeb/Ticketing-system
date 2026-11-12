using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Generates embeddings through the configured provider (<c>Ai:Provider</c>: Gemini, or any OpenAI-compatible server such as
/// a local Ollama). With <c>Ai:Enabled = false</c>, or with nothing to call, it returns null without any network traffic and
/// callers fall back to keyword search.
///
/// Two kinds of caller, two kinds of patience (<see cref="EmbeddingPriority"/>): a person waiting for a search gets an
/// immediate call and at most one short retry; background indexing is spaced out process-wide and retries for longer, so a
/// bulk run gets through a tight free-tier quota and a user's question never queues behind it.
/// </summary>
public class AiEmbeddingService : IEmbeddingService
{
    // Process-wide spacing for BACKGROUND calls only: the free-tier Google AI quota for this model is tight enough that a bulk
    // reindex firing requests back-to-back exhausts it in seconds (observed: constant 429s within the first couple of minutes
    // of a 21k-ticket reindex).
    private static readonly SemaphoreSlim BackgroundGate = new(1, 1);
    private static DateTime _lastBackgroundCallUtc = DateTime.MinValue;

    private const int BackgroundRetries = 4;
    private const int InteractiveRetries = 1;
    private static readonly TimeSpan BackgroundLongestWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InteractiveLongestWait = TimeSpan.FromSeconds(5);

    /// <summary>How long a person's question waits for the provider (<c>Ai:InteractiveEmbeddingTimeoutSeconds</c>). A bulk reindex and the indexing of a long
    /// document are not held to it: they are given the whole of the client's own limit, because nobody is looking at the screen.</summary>
    private const int DefaultInteractiveTimeoutSeconds = 12;

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiEmbeddingService> _logger;

    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public AiEmbeddingService(HttpClient httpClient, IConfiguration configuration, ILogger<AiEmbeddingService> logger, TimeProvider time)
    {
        _time = time;
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public string ModelName => AiSettings.From(_configuration).EmbeddingModelId;

    public int? Dimensions => AiSettings.From(_configuration).ExpectedDimensions;

    public bool IsAvailable => AiSettings.From(_configuration).IsAvailable;

    public Task<float[]?> EmbedAsync(string text, CancellationToken ct = default) => EmbedAsync(text, EmbeddingPriority.Interactive, ct);

    public async Task<float[]?> EmbedAsync(string text, EmbeddingPriority priority, CancellationToken ct = default)
    {
        var settings = AiSettings.From(_configuration);
        if (!settings.Enabled || settings.Provider == AiProvider.None)
        {
            _logger.LogDebug("[AI:DISABLED] Ai:Enabled is off — no embedding was requested.");
            return null;
        }
        if (!settings.IsAvailable)
        {
            _logger.LogWarning("[AI:NOT_CONFIGURED] The AI provider ({Provider}) is not fully configured (Ai:ApiKey / Ai:Endpoint / models) — embedding is disabled.", settings.Provider);
            return null;
        }
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Tickets and questions go to the provider as text: e-mail addresses and phone numbers are masked first (Ai:RedactPersonalData,
        // on by default). The same masking applies to what is indexed and to what is searched, so they still match each other.
        if (settings.RedactPersonalData)
            text = PiiRedactor.Redact(text);

        // A ticket is one chunk however long its notes are, and a provider takes only so much in one request: the beginning is cut to
        // the length a passage can ever be shown to the model, instead of the whole ticket coming back refused and never being indexed.
        text = AiPromptLimits.CutForEmbedding(text);

        var retries = priority == EmbeddingPriority.Background ? BackgroundRetries : InteractiveRetries;
        var longestWait = priority == EmbeddingPriority.Background ? BackgroundLongestWait : InteractiveLongestWait;

        var interactiveTimeout = TimeSpan.FromSeconds(Math.Clamp(_configuration.GetValue("Ai:InteractiveEmbeddingTimeoutSeconds", DefaultInteractiveTimeoutSeconds), 1, 120));

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            if (priority == EmbeddingPriority.Background)
                await SpaceOutBackgroundCallAsync(settings, ct);

            // One limit for the whole attempt (the answer and reading it), for a person's question only.
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (priority == EmbeddingPriority.Interactive) limit.CancelAfter(interactiveTimeout);

            try
            {
                using var request = BuildRequest(settings, text);
                using var response = await _httpClient.SendAsync(request, limit.Token);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // The provider tells us how long to wait, but not for ever: a Retry-After of an hour must not hold a request
                    // (or a worker) for an hour.
                    var delay = Min(GetRetryDelay(response, attempt), longestWait);
                    _logger.LogWarning(
                        "[AI:RATE_LIMITED] The embedding provider is rate-limiting us (attempt {Attempt}/{Max}), retrying in {Delay:0.0}s",
                        attempt + 1, retries + 1, delay.TotalSeconds);
                    if (attempt < retries) await Task.Delay(delay, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[AI:API_ERROR] The embedding request failed with status {Status}", response.StatusCode);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: limit.Token);
                return ReadVector(settings, doc.RootElement);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // the caller gave up (browser closed, "Stop" pressed): not an AI failure, and not worth a warning
            }
            catch (OperationCanceledException) when (limit.IsCancellationRequested)
            {
                _logger.LogWarning("[AI:TIMEOUT] The embedding provider did not answer within {Seconds} s; the question goes on without it.", interactiveTimeout.TotalSeconds);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AI:UNEXPECTED_ERROR] Failed to generate an embedding.");
                return null;
            }
        }

        _logger.LogWarning("[AI:RATE_LIMITED] The embedding request gave up after {Max} attempts due to persistent rate limiting.", retries + 1);
        return null;
    }

    private static HttpRequestMessage BuildRequest(AiSettings settings, string text)
    {
        if (settings.Provider == AiProvider.Gemini)
        {
            // Key goes in a header, not the query string, so it never appears in HttpClient's request-URI logging.
            var body = new Dictionary<string, object?> { ["content"] = new { parts = new[] { new { text } } } };
            if (settings.ConfiguredDimensions is { } size) body["outputDimensionality"] = size;
            var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Endpoint}/models/{settings.EmbeddingModel}:embedContent")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add("x-goog-api-key", settings.ApiKey);
            return request;
        }

        var payload = new Dictionary<string, object?> { ["model"] = settings.EmbeddingModel, ["input"] = text };
        if (settings.ConfiguredDimensions is { } dims) payload["dimensions"] = dims;
        var openAi = new HttpRequestMessage(HttpMethod.Post, $"{settings.Endpoint}/embeddings") { Content = JsonContent.Create(payload) };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            openAi.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
        return openAi;
    }

    private static float[] ReadVector(AiSettings settings, JsonElement root)
    {
        var values = settings.Provider == AiProvider.Gemini
            ? root.GetProperty("embedding").GetProperty("values")
            : root.GetProperty("data")[0].GetProperty("embedding");

        var result = new float[values.GetArrayLength()];
        var i = 0;
        foreach (var v in values.EnumerateArray())
            result[i++] = v.GetSingle();
        return result;
    }

    /// <summary>Enforces a process-wide minimum gap between background calls so a bulk loop can't outrun the provider's rate limit.</summary>
    private async Task SpaceOutBackgroundCallAsync(AiSettings settings, CancellationToken ct)
    {
        if (settings.BackgroundMinIntervalSeconds <= 0) return;

        await BackgroundGate.WaitAsync(ct);
        try
        {
            var wait = TimeSpan.FromSeconds(settings.BackgroundMinIntervalSeconds) - (UtcNow - _lastBackgroundCallUtc);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastBackgroundCallUtc = UtcNow;
        }
        finally
        {
            BackgroundGate.Release();
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        return TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)); // 2s, 4s, 8s, 16s
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
