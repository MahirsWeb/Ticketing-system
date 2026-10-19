using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// What the Brevo transport reports back to the outbox worker decides whether a message is tried again, so the mapping from
/// "what happened" to "what to do" is pinned here: a busy or broken provider is worth another try, a refusal of the message
/// itself is not, and e-mail that cannot or should not go out at all is skipped. The network is replaced by a stub.
/// </summary>
[Collection(ApiCollection.Name)]
public class BrevoEmailSenderTests
{
    private const string Recipient = "reader@brevo.test";

    private readonly ApiWorld _world;

    public BrevoEmailSenderTests(ApiWorld world) => _world = world;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<(string? ApiKey, string Body)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("api-key", out var key);
            Requests.Add((key?.SingleOrDefault(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return _respond(request);
        }
    }

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + " " + exception);
    }

    private static HttpResponseMessage Answer(HttpStatusCode status) => new(status) { Content = new StringContent("{}") };

    private async Task<(EmailSendResult Result, StubHandler Handler)> SendAsync(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? apiKey = "test-key",
        string? senderEmail = "noreply@sender.test",
        string to = Recipient,
        CancellationToken ct = default,
        Microsoft.Extensions.Logging.ILogger<BrevoEmailSender>? logger = null)
    {
        var handler = new StubHandler(respond);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Brevo:ApiKey"] = apiKey,
            ["Brevo:SenderEmail"] = senderEmail,
            ["Brevo:SenderName"] = "Tester"
        }).Build();

        using var scope = _world.Factory.Services.CreateScope();
        var sender = new BrevoEmailSender(
            new HttpClient(handler),
            configuration,
            logger ?? NullLogger<BrevoEmailSender>.Instance,
            scope.ServiceProvider.GetRequiredService<TicketSystemTech.Infrastructure.Persistence.AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPiiProtector>());

        return (await sender.SendAsync(to, "Subject line", "<p>Hello</p>", ct), handler);
    }

    [SqlTheory]
    [InlineData(200, EmailSendOutcome.Sent)]
    [InlineData(201, EmailSendOutcome.Sent)]
    [InlineData(429, EmailSendOutcome.Retry)]
    [InlineData(500, EmailSendOutcome.Retry)]
    [InlineData(502, EmailSendOutcome.Retry)]
    [InlineData(503, EmailSendOutcome.Retry)]
    [InlineData(400, EmailSendOutcome.Failed)]
    [InlineData(401, EmailSendOutcome.Failed)]
    [InlineData(403, EmailSendOutcome.Failed)]
    [InlineData(404, EmailSendOutcome.Failed)]
    public async Task What_the_provider_answers_decides_whether_the_message_is_tried_again(int status, EmailSendOutcome expected)
    {
        var (result, handler) = await SendAsync(_ => Answer((HttpStatusCode)status));

        Assert.Equal(expected, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [SqlFact]
    public async Task The_request_carries_the_configured_key_and_sender_and_the_message()
    {
        var (_, handler) = await SendAsync(_ => Answer(HttpStatusCode.Created));

        var (apiKey, body) = Assert.Single(handler.Requests);
        Assert.Equal("test-key", apiKey);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("noreply@sender.test", root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("Tester", root.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal(Recipient, root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("Subject line", root.GetProperty("subject").GetString());
        Assert.Equal("<p>Hello</p>", root.GetProperty("htmlContent").GetString());
    }

    [SqlFact]
    public async Task A_network_failure_is_worth_another_try()
    {
        var (result, _) = await SendAsync(_ => throw new HttpRequestException("connection refused"));

        Assert.Equal(EmailSendOutcome.Retry, result.Outcome);
        Assert.Equal(nameof(HttpRequestException), result.Detail);
    }

    [SqlFact]
    public async Task A_timeout_is_worth_another_try()
    {
        var (result, _) = await SendAsync(_ => throw new TaskCanceledException("the request timed out"));

        Assert.Equal(EmailSendOutcome.Retry, result.Outcome);
    }

    [SqlFact]
    public async Task Shutting_down_is_not_reported_as_a_failed_attempt()
    {
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }, ct: cts.Token));
    }

    [SqlFact]
    public async Task Without_an_api_key_nothing_is_sent_and_the_message_is_skipped()
    {
        var (result, handler) = await SendAsync(_ => Answer(HttpStatusCode.OK), apiKey: "");

        Assert.Equal(EmailSendOutcome.Skipped, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [SqlFact]
    public async Task Without_a_sender_address_nothing_is_sent_and_the_message_is_skipped()
    {
        var (result, handler) = await SendAsync(_ => Answer(HttpStatusCode.OK), senderEmail: "");

        Assert.Equal(EmailSendOutcome.Skipped, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [SqlFact]
    public async Task Nothing_is_sent_to_an_account_that_has_been_deactivated()
    {
        var person = await _world.CreateActorAsync("brevoInactive", UserRole.Client, null, _world.CompanyId);
        try
        {
            var active = await SendAsync(_ => Answer(HttpStatusCode.OK), to: person.Email);
            Assert.Equal(EmailSendOutcome.Sent, active.Result.Outcome);

            await _world.Db(db => db.Users.Where(u => u.Id == person.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

            var (result, handler) = await SendAsync(_ => Answer(HttpStatusCode.OK), to: person.Email);
            Assert.Equal(EmailSendOutcome.Skipped, result.Outcome);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            await _world.Db(db => db.Users.Where(u => u.Id == person.Id).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task The_recipients_address_never_reaches_the_log()
    {
        var person = await _world.CreateActorAsync("brevoLogged", UserRole.Client, null, _world.CompanyId);
        try
        {
            var log = new CapturingLogger<BrevoEmailSender>();
            await SendAsync(_ => Answer(HttpStatusCode.ServiceUnavailable), to: person.Email, logger: log);
            await SendAsync(_ => throw new HttpRequestException("connection refused"), to: person.Email, logger: log);
            await SendAsync(_ => Answer(HttpStatusCode.OK), to: person.Email, apiKey: "", logger: log);
            await SendAsync(_ => Answer(HttpStatusCode.OK), to: person.Email, senderEmail: "", logger: log);
            await _world.Db(db => db.Users.Where(u => u.Id == person.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
            await SendAsync(_ => Answer(HttpStatusCode.OK), to: person.Email, logger: log);

            Assert.True(log.Lines.Count >= 5, "every path should have logged something");
            Assert.All(log.Lines, line => Assert.DoesNotContain(person.Email, line, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await _world.Db(db => db.Users.Where(u => u.Id == person.Id).ExecuteDeleteAsync());
        }
    }
}
