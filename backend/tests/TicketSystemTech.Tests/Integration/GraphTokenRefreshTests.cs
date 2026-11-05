using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// TS-31.3: two requests that find a mailbox's access token expired at the same moment used to both go to Microsoft's token
/// endpoint, and the last one to save won. The real service, a real database, and a Microsoft that only counts what it is asked.
/// </summary>
[Collection(ApiCollection.Name)]
public class GraphTokenRefreshTests
{
    private readonly ApiWorld _world;

    public GraphTokenRefreshTests(ApiWorld world) => _world = world;

    private sealed class FakeMicrosoft : HttpMessageHandler
    {
        public int TokenRequests;
        public int InboxRequests;
        private int _issued;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host.Contains("login.microsoftonline.com"))
            {
                Interlocked.Increment(ref TokenRequests);
                await Task.Delay(300, cancellationToken); // long enough for the other request to arrive while this one is still away
                var n = Interlocked.Increment(ref _issued);
                return Json($$"""{"access_token":"access-{{n}}","refresh_token":"refresh-{{n}}","expires_in":3600}""");
            }
            Interlocked.Increment(ref InboxRequests);
            return Json("""{"value":[]}""");
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [SqlFact]
    public async Task Two_requests_that_find_the_token_expired_at_once_refresh_it_once()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 3, 14, 9, 0, 0, TimeSpan.Zero));
        var keys = DataProtectionProvider.Create("graph-refresh-test");
        var protector = keys.CreateProtector("EmailConnection.OAuthTokens");
        var microsoft = new FakeMicrosoft();

        using var scope = _world.Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        var pii = scope.ServiceProvider.GetRequiredService<IPiiProtector>();

        AppDbContext NewContext() => new(options, pii);
        GraphEmailIntegrationService NewService(AppDbContext db) => new(
            db, new HttpClient(microsoft), null!, Options.Create(new MicrosoftGraphOptions { ClientId = "id", ClientSecret = "secret" }),
            NullLogger<GraphEmailIntegrationService>.Instance, keys, null!, clock);

        var department = new Department { Name = "graph " + Guid.NewGuid().ToString("N")[..6], Email = "graph-test@example.test" };
        await using (var db = NewContext())
        {
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            db.EmailConnections.Add(new EmailConnection
            {
                DepartmentId = department.Id,
                ConnectedByUserId = _world.Admin.Id,
                ConnectedEmail = "servis@example.test",
                AccessToken = protector.Protect("expired-access"),
                RefreshToken = protector.Protect("refresh-0"),
                AccessTokenExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await using var first = NewContext();
            await using var second = NewContext();

            await Task.WhenAll(NewService(first).ListInboxAsync(department.Id, 5), NewService(second).ListInboxAsync(department.Id, 5));

            Assert.Equal(1, microsoft.TokenRequests);
            Assert.Equal(2, microsoft.InboxRequests);

            // What was stored is the one answer that Microsoft gave, not a mix of two.
            await using var read = NewContext();
            var stored = await read.EmailConnections.AsNoTracking().SingleAsync(c => c.DepartmentId == department.Id);
            Assert.Equal("access-1", protector.Unprotect(stored.AccessToken));
            Assert.Equal("refresh-1", protector.Unprotect(stored.RefreshToken));
            Assert.True(stored.AccessTokenExpiresAtUtc > clock.GetUtcNow().UtcDateTime.AddMinutes(30));
        }
        finally
        {
            await using var cleanup = NewContext();
            await cleanup.EmailConnections.Where(c => c.DepartmentId == department.Id).ExecuteDeleteAsync();
            await cleanup.Departments.Where(d => d.Id == department.Id).ExecuteDeleteAsync();
        }
    }
}
