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

    [Fact]
    public async Task Email_is_never_sent_twice_even_when_the_provider_answers_with_a_server_error() =>
        Assert.Equal(1, await CallsMadeAsync("IEmailSender", HttpMethod.Post, HttpStatusCode.ServiceUnavailable));

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
