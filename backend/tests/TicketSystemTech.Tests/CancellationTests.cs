using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-40: a request the browser gave up on kept running (and AI calls reported it as an AI failure).</summary>
public class CancellationTests
{
    private static IConfiguration ConfigWithKey() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GoogleAi:ApiKey"] = "test-key" }).Build();

    /// <summary>Answers only when told to; otherwise waits until the request is cancelled, like a slow remote API.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task A_cancelled_embedding_request_propagates_instead_of_returning_null()
    {
        var handler = new HangingHandler();
        var service = new GoogleAiEmbeddingService(new HttpClient(handler), ConfigWithKey(), NullLogger<GoogleAiEmbeddingService>.Instance);
        using var cts = new CancellationTokenSource();

        var pending = service.EmbedAsync("printer ne štampa", cts.Token);
        while (handler.Calls == 0) await Task.Delay(10); // the request is now in flight
        cts.Cancel();

        // Before: the generic catch swallowed this, logged "[AI:UNEXPECTED_ERROR]" and returned null, so the caller
        // carried on with a keyword search for a user who had already left.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task A_cancelled_chat_request_propagates_instead_of_returning_null()
    {
        var handler = new HangingHandler();
        var service = new GeminiChatCompletionService(new HttpClient(handler), ConfigWithKey(), NullLogger<GeminiChatCompletionService>.Instance);
        using var cts = new CancellationTokenSource();

        var pending = service.AskAsync("system", "context", "question", cts.Token);
        while (handler.Calls == 0) await Task.Delay(10);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task A_real_failure_is_still_reported_as_null_so_callers_can_fall_back()
    {
        var service = new GeminiChatCompletionService(
            new HttpClient(new ThrowingHandler()), ConfigWithKey(), NullLogger<GeminiChatCompletionService>.Instance);

        Assert.Null(await service.AskAsync("system", "context", "question"));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
