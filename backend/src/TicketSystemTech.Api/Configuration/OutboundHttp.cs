using Microsoft.Extensions.Http.Resilience;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Api.Configuration;

/// <summary>
/// Every outbound integration gets its own HttpClient with limits suited to it. They all used to share one unnamed
/// client with a 100 s timeout and no retry, so a slow Gemini or Microsoft response could hold a request thread for
/// over a minute and a provider outage kept being hit on every request.
///
/// Per client: a timeout for each attempt and one for the whole call, retries only where repeating the request is
/// safe, and a circuit breaker that stops calling a provider that has been failing (and answers fast instead).
/// </summary>
public static class OutboundHttp
{
    public static IServiceCollection AddOutboundHttpClients(this IServiceCollection services)
    {
        // Email: a POST that timed out may still have been delivered, so it is never repeated (a second attempt could
        // mail the same person twice). Short limits: nobody is waiting on this, and callers treat it as best-effort.
        services.AddHttpClient<IEmailSender, BrevoEmailSender>()
            .AddStandardResilienceHandler(o =>
            {
                Limits(o, attempt: TimeSpan.FromSeconds(10), total: TimeSpan.FromSeconds(15));
                NoAutomaticRetry(o);
            });

        // Chat completion: an LLM answer can legitimately take tens of seconds. Generating text is safe to repeat, so one
        // retry covers a transient 429/5xx.
        services.AddHttpClient<IChatCompletionService, GeminiChatCompletionService>(c => c.Timeout = TimeSpan.FromMinutes(2))
            .AddStandardResilienceHandler(o =>
            {
                Limits(o, attempt: TimeSpan.FromSeconds(45), total: TimeSpan.FromSeconds(100));
                o.Retry.MaxRetryAttempts = 1;
            });

        // Embeddings: GoogleAiEmbeddingService already throttles and retries 429s itself (honouring Retry-After). A second
        // retry layer underneath would multiply the attempts, so here only the timeouts and the breaker apply.
        services.AddHttpClient<IEmbeddingService, GoogleAiEmbeddingService>(c => c.Timeout = TimeSpan.FromMinutes(2))
            .AddStandardResilienceHandler(o =>
            {
                Limits(o, attempt: TimeSpan.FromSeconds(30), total: TimeSpan.FromSeconds(40));
                NoAutomaticRetry(o);
            });

        // Microsoft 365: the pipeline does not retry, because it cannot tell the harmless GET reads from the OAuth token POST
        // (an automatic repeat of a token request is not something to do blindly). GraphEmailIntegrationService retries its
        // own GET reads instead.
        services.AddHttpClient<IEmailIntegrationService, GraphEmailIntegrationService>()
            .AddStandardResilienceHandler(o =>
            {
                Limits(o, attempt: TimeSpan.FromSeconds(30), total: TimeSpan.FromSeconds(60));
                NoAutomaticRetry(o);
            });

        return services;
    }

    /// <summary>Keeps the timeouts and the circuit breaker but never repeats a request on its own.</summary>
    private static void NoAutomaticRetry(HttpStandardResilienceOptions o)
    {
        o.Retry.MaxRetryAttempts = 1; // the option cannot be 0; ShouldHandle below makes it moot
        o.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
    }

    private static void Limits(HttpStandardResilienceOptions o, TimeSpan attempt, TimeSpan total)
    {
        o.AttemptTimeout.Timeout = attempt;
        o.TotalRequestTimeout.Timeout = total;

        // The default breaker needs 100 calls in the window before it ever trips, which at this volume is never.
        o.CircuitBreaker.MinimumThroughput = 5;
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        // Must be at least twice the attempt timeout, or the options are rejected.
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(30).Ticks, attempt.Ticks * 2));
    }
}
