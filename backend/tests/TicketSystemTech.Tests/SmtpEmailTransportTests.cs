using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests;

/// <summary>
/// What the SMTP transport reports back to the outbox worker decides whether a message is tried again, so the mapping from
/// "what the server said" to "what to do" is pinned here against a real SMTP conversation (MailKit talking to a small
/// server on the loopback interface): a busy or unreachable server is worth another try, a refusal of the message itself is
/// not, and mail that cannot be sent because nothing is configured is skipped.
/// </summary>
public class SmtpEmailTransportTests : IDisposable
{
    private const string Recipient = "reader@smtp.test";
    private readonly FakeSmtpServer _server = new();

    public void Dispose() => _server.Dispose();

    private static IConfiguration Config(int port, Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "127.0.0.1",
            ["Smtp:Port"] = port.ToString(),
            ["Smtp:Security"] = "None",
            ["Smtp:FromAddress"] = "noreply@sender.test",
            ["Smtp:FromName"] = "Tester",
            ["Smtp:TimeoutSeconds"] = "5"
        };
        if (overrides is not null) foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private Task<EmailSendResult> SendAsync(Dictionary<string, string?>? overrides = null, string to = Recipient, int? port = null,
        ILogger<SmtpEmailTransport>? logger = null, CancellationToken ct = default) =>
        new SmtpEmailTransport(Config(port ?? _server.Port, overrides), logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SmtpEmailTransport>.Instance)
            .SendAsync(to, "Subject line", "<p>Hello <b>there</b></p>", ct);

    // ======================= delivery =======================

    [Fact]
    public async Task A_message_is_delivered_with_sender_recipient_subject_and_both_text_and_html()
    {
        var result = await SendAsync();

        Assert.Equal(EmailSendOutcome.Sent, result.Outcome);
        var mail = Assert.Single(_server.Mails);
        Assert.Equal("noreply@sender.test", mail.From);
        Assert.Equal(new[] { Recipient }, mail.To);

        var parsed = MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(mail.Data)));
        Assert.Equal("Subject line", parsed.Subject);
        Assert.Equal("Tester", parsed.From.Mailboxes.Single().Name);
        Assert.Equal("<p>Hello <b>there</b></p>", parsed.HtmlBody);
        Assert.Equal("Hello there", parsed.TextBody);
    }

    [Fact]
    public async Task A_reply_to_address_is_added_when_configured()
    {
        await SendAsync(new() { ["Smtp:ReplyTo"] = "help@sender.test" });

        var parsed = MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Assert.Single(_server.Mails).Data)));
        Assert.Equal("help@sender.test", parsed.ReplyTo.Mailboxes.Single().Address);
    }

    [Fact]
    public async Task The_configured_login_is_used_and_a_wrong_one_is_retried_not_given_up()
    {
        _server.RequiredLogin = ("mailer", "s3cret");

        var good = await SendAsync(new() { ["Smtp:Username"] = "mailer", ["Smtp:Password"] = "s3cret" });
        Assert.Equal(EmailSendOutcome.Sent, good.Outcome);

        var wrong = await SendAsync(new() { ["Smtp:Username"] = "mailer", ["Smtp:Password"] = "nope" });
        Assert.Equal(EmailSendOutcome.Retry, wrong.Outcome); // the operator fixes the setting; the mail then still goes out
        Assert.Contains("login", wrong.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_server.Mails);
    }

    // ======================= what the server answers =======================

    [Theory]
    [InlineData("rcpt", "450 4.2.1 mailbox busy", EmailSendOutcome.Retry)]
    [InlineData("rcpt", "451 4.3.0 try again later", EmailSendOutcome.Retry)]
    [InlineData("rcpt", "550 5.1.1 no such user", EmailSendOutcome.Failed)]
    [InlineData("rcpt", "553 5.1.3 bad address syntax", EmailSendOutcome.Failed)]
    [InlineData("data", "451 4.3.0 local error", EmailSendOutcome.Retry)]
    [InlineData("data", "554 5.7.1 rejected as spam", EmailSendOutcome.Failed)]
    public async Task What_the_server_answers_decides_whether_the_message_is_tried_again(string stage, string reply, EmailSendOutcome expected)
    {
        if (stage == "rcpt") _server.RcptReply = reply; else _server.DataReply = reply;

        var result = await SendAsync();

        Assert.Equal(expected, result.Outcome);
        Assert.Empty(_server.Mails);
    }

    [Fact]
    public async Task A_server_that_is_not_there_is_worth_another_try()
    {
        var result = await SendAsync(port: FakeSmtpServer.UnusedPort());

        Assert.Equal(EmailSendOutcome.Retry, result.Outcome);
    }

    [Fact]
    public async Task A_server_that_hangs_up_is_worth_another_try()
    {
        _server.HangUpAfterGreeting = true;

        Assert.Equal(EmailSendOutcome.Retry, (await SendAsync()).Outcome);
    }

    [Fact]
    public async Task A_server_that_never_answers_is_given_up_on_after_the_timeout_and_retried_later()
    {
        _server.StaySilent = true;
        var started = DateTime.UtcNow;

        var result = await SendAsync(new() { ["Smtp:TimeoutSeconds"] = "1" });

        Assert.Equal(EmailSendOutcome.Retry, result.Outcome);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "the timeout must apply");
    }

    [Fact]
    public async Task Required_encryption_that_the_server_does_not_offer_is_not_silently_skipped()
    {
        var result = await SendAsync(new() { ["Smtp:Security"] = "StartTls" });

        Assert.Equal(EmailSendOutcome.Retry, result.Outcome); // a setting problem: never downgraded to plain text
        Assert.Empty(_server.Mails);
    }

    [Fact]
    public async Task Shutting_down_is_not_reported_as_a_failed_attempt()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(ct: cts.Token));
    }

    // ======================= nothing to send with =======================

    [Fact]
    public async Task Without_a_host_nothing_is_sent_and_the_message_is_skipped()
    {
        var result = await SendAsync(new() { ["Smtp:Host"] = "" });

        Assert.Equal(EmailSendOutcome.Skipped, result.Outcome);
        Assert.Equal(0, _server.Connections);
    }

    [Fact]
    public async Task Without_a_sender_address_nothing_is_sent_and_the_message_is_skipped()
    {
        var result = await SendAsync(new() { ["Smtp:FromAddress"] = "" });

        Assert.Equal(EmailSendOutcome.Skipped, result.Outcome);
        Assert.Equal(0, _server.Connections);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("two@@signs.test")]
    [InlineData("")]
    public async Task An_address_that_cannot_be_an_address_is_refused_without_calling_the_server(string recipient)
    {
        var result = await SendAsync(to: recipient);

        Assert.Equal(EmailSendOutcome.Failed, result.Outcome);
        Assert.Equal(0, _server.Connections);
    }

    // ======================= privacy =======================

    [Fact]
    public async Task The_recipients_address_never_reaches_the_log()
    {
        var log = new CapturingLogger<SmtpEmailTransport>();
        _server.RcptReply = "450 busy";
        await SendAsync(logger: log);
        _server.RcptReply = "550 no such user";
        await SendAsync(logger: log);
        _server.RcptReply = "250 OK";
        _server.RequiredLogin = ("a", "b");
        await SendAsync(new() { ["Smtp:Username"] = "a", ["Smtp:Password"] = "wrong" }, logger: log);
        await SendAsync(port: FakeSmtpServer.UnusedPort(), logger: log);
        await SendAsync(new() { ["Smtp:Host"] = "" }, logger: log);
        await SendAsync(new() { ["Smtp:FromAddress"] = "" }, logger: log);

        Assert.True(log.Lines.Count >= 6, "every path should have logged something");
        Assert.All(log.Lines, line => Assert.DoesNotContain("smtp.test", line, StringComparison.OrdinalIgnoreCase));
        Assert.All(log.Lines, line => Assert.DoesNotContain("wrong", line, StringComparison.OrdinalIgnoreCase)); // nor the password
    }

    // ======================= settings =======================

    [Theory]
    [InlineData("None", SecureSocketOptions.None)]
    [InlineData("starttls", SecureSocketOptions.StartTls)]
    [InlineData("StartTls", SecureSocketOptions.StartTls)]
    [InlineData("Ssl", SecureSocketOptions.SslOnConnect)]
    [InlineData("tls", SecureSocketOptions.SslOnConnect)]
    public void The_security_setting_accepts_the_documented_spellings(string text, SecureSocketOptions expected) =>
        Assert.Equal(expected, SmtpSettings.ParseSecurity(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("maybe")]
    public void Anything_else_is_not_a_security_setting(string? text) => Assert.Null(SmtpSettings.ParseSecurity(text));

    [Fact]
    public void Without_a_security_setting_encryption_is_required_rather_than_optional()
    {
        var settings = SmtpSettings.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:Host"] = "mail.test" }).Build());

        Assert.Equal(SecureSocketOptions.StartTls, settings.Security);
        Assert.Equal(587, settings.Port);
    }

    [Fact]
    public void The_plain_text_version_keeps_the_paragraphs_and_drops_the_markup()
    {
        var text = SmtpEmailTransport.HtmlToText("<h1>Title</h1><p>First &amp; second</p><p>Line<br>break</p>");

        Assert.Equal("Title\nFirst & second\nLine\nbreak", text);
    }
}
