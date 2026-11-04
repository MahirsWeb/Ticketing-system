using System.Net.Sockets;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Delivers e-mail through any SMTP server (section <c>Smtp</c>): the company's own mail server, a relay, or a development
/// catcher such as smtp4dev. Only the outbox worker calls it, and it reports what happened instead of hiding failures,
/// because "try again later" and "do not bother" are different answers for the worker.
///
/// The recipient's address is never written to the log (the worker logs the outbox message id).
/// </summary>
public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailTransport> _logger;

    public SmtpEmailTransport(IConfiguration configuration, ILogger<SmtpEmailTransport> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var settings = SmtpSettings.From(_configuration);

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            _logger.LogWarning("Smtp:Host is not configured — an email with subject '{Subject}' was NOT sent.", subject);
            return EmailSendResult.Skipped("Smtp:Host is not configured.");
        }

        // There is no default sender address: a server accepts mail only from addresses it is willing to relay for, so any
        // address picked here would be refused, and a real one in the source code would be somebody's private address.
        if (string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            _logger.LogWarning("Smtp:FromAddress is not configured — an email with subject '{Subject}' was NOT sent.", subject);
            return EmailSendResult.Skipped("Smtp:FromAddress is not configured.");
        }

        MimeMessage message;
        try
        {
            message = BuildMessage(settings, toEmail, subject, htmlBody);
        }
        catch (ParseException ex)
        {
            // An address that cannot be written down as an address will never be accepted by any server.
            _logger.LogWarning(ex, "An email with subject '{Subject}' has an address that cannot be used.", subject);
            return EmailSendResult.Failed("An address in the message is not a valid e-mail address.");
        }

        try
        {
            using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
            await client.ConnectAsync(settings.Host, settings.Port, settings.Security, ct);
            if (!string.IsNullOrEmpty(settings.Username))
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return EmailSendResult.Sent();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutting down: the message stays in the outbox and is picked up after the restart
        }
        catch (SmtpCommandException ex)
        {
            // 4xx: the server could not take it right now (busy, greylisted, mailbox temporarily unavailable). 5xx: the
            // server refused this message for good (unknown mailbox, policy), and asking again will get the same answer.
            var code = (int)ex.StatusCode;
            _logger.LogWarning("The SMTP server answered {Code} to an email with subject '{Subject}'.", code, subject);
            var detail = $"The SMTP server answered {code}";
            return code is >= 400 and < 500 ? EmailSendResult.Retry(detail) : EmailSendResult.Failed(detail);
        }
        catch (AuthenticationException ex)
        {
            // A wrong user name or password is something the operator fixes in the settings. Giving up now would throw away
            // mail that will go out as soon as they do, so it is retried like an outage.
            _logger.LogError(ex, "The SMTP server refused the login in Smtp:Username / Smtp:Password.");
            return EmailSendResult.Retry("The SMTP server refused the login (check Smtp:Username and Smtp:Password).");
        }
        catch (Exception ex)
        {
            // Network errors, timeouts, a server that hung up, TLS that was required and not offered, anything unexpected:
            // the message did not go out, and nothing says it never will.
            _logger.LogError(ex, "Sending an email through the SMTP server did not finish ({Reason})", ex.GetType().Name);
            return EmailSendResult.Retry(ex.GetType().Name);
        }
    }

    private static MimeMessage BuildMessage(SmtpSettings settings, string toEmail, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress!));
        message.To.Add(MailboxAddress.Parse(toEmail));
        if (!string.IsNullOrWhiteSpace(settings.ReplyTo)) message.ReplyTo.Add(MailboxAddress.Parse(settings.ReplyTo));
        message.Subject = subject;

        // Both parts, so a client that shows plain text only (or a filter that prefers it) still gets the message.
        var body = new BodyBuilder { HtmlBody = htmlBody, TextBody = HtmlToText(htmlBody) };
        message.Body = body.ToMessageBody();
        return message;
    }

    /// <summary>A readable plain-text version of the notification e-mails (short, simple HTML).</summary>
    public static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<\s*(br|/p|/div|/h\d|/li)\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<.*?>", string.Empty, RegexOptions.Singleline);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+", " ");
        return Regex.Replace(text, @"\n\s*\n+", "\n\n").Trim();
    }
}

/// <summary>The <c>Smtp</c> section, read at the moment of each send so a changed setting applies without a restart.</summary>
public sealed record SmtpSettings(
    string? Host, int Port, SecureSocketOptions Security, string? Username, string? Password,
    string? FromAddress, string FromName, string? ReplyTo, int TimeoutSeconds)
{
    public static SmtpSettings From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Smtp");
        var security = ParseSecurity(section["Security"]) ?? SecureSocketOptions.StartTls;
        return new SmtpSettings(
            section["Host"]?.Trim(),
            section.GetValue("Port", 587),
            security,
            section["Username"],
            section["Password"],
            section["FromAddress"]?.Trim(),
            string.IsNullOrWhiteSpace(section["FromName"]) ? "Ticket System Tech" : section["FromName"]!.Trim(),
            section["ReplyTo"]?.Trim(),
            Math.Clamp(section.GetValue("TimeoutSeconds", 20), 1, 300));
    }

    /// <summary>The accepted spellings of <c>Smtp:Security</c>; null means the text is not one of them.</summary>
    public static SecureSocketOptions? ParseSecurity(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "none" => SecureSocketOptions.None,
        "starttls" => SecureSocketOptions.StartTls,
        "ssl" or "tls" or "sslonconnect" => SecureSocketOptions.SslOnConnect,
        _ => null
    };
}
