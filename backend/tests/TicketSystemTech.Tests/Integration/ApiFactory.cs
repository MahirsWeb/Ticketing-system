using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The real application (controllers, middleware, Identity, EF Core, the real migrations) running in memory against a throwaway
/// SQL Server database. Only the three things that would reach the outside world are replaced by fakes.
/// Configuration comes from environment variables, set by <see cref="ApiWorld"/> before the host is built: a value added
/// from here would arrive too late for what Program.cs reads while it is still building the host.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeEmbeddingService Embeddings { get; } = new();
    public FakeChatCompletionService Chat { get; } = new();
    /// <summary>The wire to the mail server. Everything above it is real: the application files e-mail in the outbox table as it does in
    /// production, and the tests run the worker by hand against this fake.</summary>
    public FakeEmailTransport Transport { get; } = new();
    /// <summary>What the application asked the database, for tests that say how many queries something costs.</summary>
    public CommandLog Commands { get; } = new();
    /// <summary>The real-time events that were pushed (nobody is connected in a test, so the real publisher reaches no one).</summary>
    public RecordingPublisher Pushes { get; } = new();
    /// <summary>The wire to Microsoft Graph (the branch's mailbox). By default it has nothing in it; a test says what it answers.</summary>
    public FakeMicrosoftGraph Graph { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmbeddingService>();
            services.AddSingleton<IEmbeddingService>(Embeddings);
            services.RemoveAll<IChatCompletionService>();
            services.AddSingleton<IChatCompletionService>(Chat);
            services.RemoveAll<IEmailTransport>();
            services.AddSingleton<IEmailTransport>(Transport);
            services.RemoveAll<INotificationPublisher>();
            services.AddSingleton<INotificationPublisher>(Pushes);
            services.AddHttpClient<IEmailIntegrationService, GraphEmailIntegrationService>().ConfigurePrimaryHttpMessageHandler(() => Graph);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(Commands));
        });
    }
}

/// <summary>Microsoft Graph, as far as the mailbox service can tell: it answers a request for a message's attachments with what the test put in it, or fails.</summary>
public sealed class FakeMicrosoftGraph : HttpMessageHandler
{
    public string AttachmentsJson { get; set; } = """{"value":[]}""";
    public bool Down { get; set; }
    public int Requests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Requests);
        if (Down) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(AttachmentsJson, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
