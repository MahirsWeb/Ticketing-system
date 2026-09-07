using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>Generates embeddings via Google AI Studio's gemini-embedding-001 model, when configured.</summary>
public class GoogleAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleAiEmbeddingService> _logger;

    // Process-wide throttle: the free-tier Google AI quota for this model is tight enough that a bulk
    // reindex firing requests back-to-back exhausts it in seconds (observed: constant 429s within the
    // first couple of minutes of a 21k-ticket reindex). A shared minimum interval between calls — plus
    // retrying on 429 instead of giving up — keeps single interactive calls (chat, ticket close) working
    // as before while making bulk reindexing actually get through the backlog instead of stalling at ~0%.
    private static readonly SemaphoreSlim _throttleLock = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2.5);
    private const int MaxRetries = 4;

    public GoogleAiEmbeddingService(HttpClient httpClient, IConfiguration configuration, ILogger<GoogleAiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        var apiKey = _configuration["GoogleAi:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[AI:NOT_CONFIGURED] GoogleAi:ApiKey is not set — embedding is disabled.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={apiKey}";

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await ThrottleAsync(ct);
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, new
                {
                    content = new { parts = new[] { new { text } } }
                }, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var delay = GetRetryDelay(response, attempt);
                    _logger.LogWarning(
                        "[AI:RATE_LIMITED] Google AI embedding rate-limited (attempt {Attempt}/{Max}), retrying in {Delay:0.0}s",
                        attempt + 1, MaxRetries + 1, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[AI:API_ERROR] Google AI embedding request failed with status {Status}", response.StatusCode);
                    return null;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
                var values = doc.RootElement.GetProperty("embedding").GetProperty("values");
                var result = new float[values.GetArrayLength()];
                var i = 0;
                foreach (var v in values.EnumerateArray())
                    result[i++] = v.GetSingle();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AI:UNEXPECTED_ERROR] Failed to generate embedding via Google AI.");
                return null;
            }
        }

        _logger.LogWarning("[AI:RATE_LIMITED] Google AI embedding gave up after {Max} retries due to persistent rate limiting.", MaxRetries + 1);
        return null;
    }

    /// <summary>Enforces a process-wide minimum gap between calls so a bulk loop can't outrun the API's rate limit.</summary>
    private static async Task ThrottleAsync(CancellationToken ct)
    {
        await _throttleLock.WaitAsync(ct);
        try
        {
            var wait = MinInterval - (DateTime.UtcNow - _lastCallUtc);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCallUtc = DateTime.UtcNow;
        }
        finally
        {
            _throttleLock.Release();
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        return TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)); // 2s, 4s, 8s, 16s
    }
}
