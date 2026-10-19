using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Delivers e-mail through the Brevo (formerly Sendinblue) HTTP API. It is only called by the outbox worker, which is why it
/// reports what happened instead of hiding failures: "try again later" and "do not bother" are different answers.
/// The recipient's address is never written to the log (the outbox row id, logged by the worker, identifies the message).
/// </summary>
public class BrevoEmailSender : IEmailTransport
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BrevoEmailSender> _logger;
    private readonly AppDbContext _db;
    private readonly IPiiProtector _piiProtector;

    public BrevoEmailSender(HttpClient httpClient, IConfiguration configuration, ILogger<BrevoEmailSender> logger, AppDbContext db, IPiiProtector piiProtector)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _db = db;
        _piiProtector = piiProtector;
    }

    public async Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Centralized guard: an inactive account's mailbox no longer receives anything from the app,
        // regardless of which flow triggered the send (ticket update, task assignment, invite, ...).
        var normalizedEmail = _piiProtector.Hash(toEmail);
        var recipientIsInactive = await _db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsActive, ct);
        if (recipientIsInactive)
        {
            _logger.LogInformation("Skipped an email — the recipient's account is inactive.");
            return EmailSendResult.Skipped("The recipient's account is inactive.");
        }

        var apiKey = _configuration["Brevo:ApiKey"];
        var senderEmail = _configuration["Brevo:SenderEmail"];
        var senderName = _configuration["Brevo:SenderName"] ?? "Ticket System Tech";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Brevo:ApiKey is not configured — an email with subject '{Subject}' was NOT sent.", subject);
            return EmailSendResult.Skipped("Brevo:ApiKey is not configured.");
        }

        // There is no default sender address: Brevo only sends from an address that has been verified in the account, so any
        // address picked here would be refused, and a real one in the source code would be somebody's private address.
        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            _logger.LogWarning("Brevo:SenderEmail is not configured — an email with subject '{Subject}' was NOT sent.", subject);
            return EmailSendResult.Skipped("Brevo:SenderEmail is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            sender = new { name = senderName, email = senderEmail },
            to = new[] { new { email = toEmail } },
            subject,
            htmlContent = htmlBody
        });

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return EmailSendResult.Sent();

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Brevo email send failed ({Status}): {Body}", response.StatusCode, body);

            // The provider is busy or broken: another try later may work. A refusal of the message itself (bad address,
            // unverified sender, invalid key) will be refused again, so it is final.
            var detail = $"Brevo answered {(int)response.StatusCode}";
            return response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500
                ? EmailSendResult.Retry(detail)
                : EmailSendResult.Failed(detail);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutting down: the message stays in the outbox and is picked up after the restart
        }
        catch (Exception ex)
        {
            // Network errors, timeouts, and the resilience pipeline's own exceptions (an open circuit breaker is one): the
            // message did not go out, nothing says it never will.
            _logger.LogError(ex, "Brevo email send did not finish ({Reason})", ex.GetType().Name);
            return EmailSendResult.Retry(ex.GetType().Name);
        }
    }
}
