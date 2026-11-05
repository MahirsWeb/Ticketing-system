using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Api.Configuration;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-16: outbound calls had a 100 s timeout, no retry policy and no circuit breaker.</summary>
public class OutboundHttpTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public int Calls;

        public CountingHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }

    /// <summary>Builds the real client pipeline for one integration, with the network replaced by a counting fake.
    /// This also proves the resilience options are valid: invalid ones throw when the pipeline is first used.</summary>
    private static async Task<int> CallsMadeAsync(string clientName, HttpMethod method, HttpStatusCode answer)
    {
        var handler = new CountingHandler(answer);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboundHttpClients();
        services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        using var request = new HttpRequestMessage(method, "https://provider.example/api");
        using var response = await client.SendAsync(request);

        Assert.Equal(answer, response.StatusCode);
        return handler.Calls;
    }

    /// <summary>Sends <paramref name="times"/> requests one after another and says how many of them reached the network.</summary>
    private static async Task<(int ReachedTheNetwork, int RefusedByTheBreaker)> ManyCallsAsync(string clientName, HttpStatusCode answer, int times)
    {
        var handler = new CountingHandler(answer);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboundHttpClients();
        services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        var refused = 0;
        for (var i = 0; i < times; i++)
        {
            try
            {
                using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://provider.example/api"));
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException)
            {
                refused++;
            }
        }
        return (handler.Calls, refused);
    }

    // NOVO-ARC-04
    [Fact]
    public async Task Rate_limits_from_the_embedding_provider_never_open_the_circuit_breaker()
    {
        var (reached, refused) = await ManyCallsAsync("IEmbeddingService", HttpStatusCode.TooManyRequests, 15);

        Assert.Equal(15, reached);
        Assert.Equal(0, refused);
    }

    [Fact]
    public async Task A_provider_that_keeps_failing_does_open_it_so_that_nobody_waits_for_a_dead_service()
    {
        var (reached, refused) = await ManyCallsAsync("IEmbeddingService", HttpStatusCode.ServiceUnavailable, 15);

        Assert.True(refused > 0, "the breaker never opened");
        Assert.True(reached < 15);
    }

    [Fact]
    public async Task Email_is_never_sent_twice_even_when_the_provider_answers_with_a_server_error() =>
        Assert.Equal(1, await CallsMadeAsync("IEmailTransport", HttpMethod.Post, HttpStatusCode.ServiceUnavailable));

    [Fact]
    public async Task Embedding_requests_are_not_repeated_by_the_pipeline_because_the_service_retries_rate_limits_itself() =>
        Assert.Equal(1, await CallsMadeAsync("IEmbeddingService", HttpMethod.Post, HttpStatusCode.ServiceUnavailable));

    [Fact]
    public async Task Microsoft_requests_are_not_repeated_by_the_pipeline_so_a_token_request_is_never_replayed() =>
        Assert.Equal(1, await CallsMadeAsync("IEmailIntegrationService", HttpMethod.Post, HttpStatusCode.ServiceUnavailable));

    [Fact]
    public async Task A_chat_generation_is_retried_once_on_a_transient_failure() =>
        Assert.Equal(2, await CallsMadeAsync("IChatCompletionService", HttpMethod.Post, HttpStatusCode.ServiceUnavailable));

    [Fact]
    public async Task A_successful_answer_is_a_single_call() =>
        Assert.Equal(1, await CallsMadeAsync("IChatCompletionService", HttpMethod.Post, HttpStatusCode.OK));

    [Fact]
    public async Task A_client_error_is_not_retried() =>
        Assert.Equal(1, await CallsMadeAsync("IChatCompletionService", HttpMethod.Post, HttpStatusCode.BadRequest));
}
