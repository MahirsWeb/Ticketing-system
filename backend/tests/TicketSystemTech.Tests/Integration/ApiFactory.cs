using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketSystemTech.Application.Common.Interfaces;

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
    public RecordingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmbeddingService>();
            services.AddSingleton<IEmbeddingService>(Embeddings);
            services.RemoveAll<IChatCompletionService>();
            services.AddSingleton<IChatCompletionService>(Chat);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }
}
