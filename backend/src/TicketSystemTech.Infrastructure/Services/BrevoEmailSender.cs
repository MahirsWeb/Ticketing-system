using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>Sends transactional emails through the Brevo (formerly Sendinblue) HTTP API.</summary>
public class BrevoEmailSender : IEmailSender
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

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Centralized guard: an inactive account's mailbox no longer receives anything from the app,
        // regardless of which flow triggered the send (ticket update, task assignment, invite, ...).
        var normalizedEmail = _piiProtector.Hash(toEmail);
        var recipientIsInactive = await _db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsActive, ct);
        if (recipientIsInactive)
        {
            _logger.LogInformation("Skipped email to {Email} — recipient account is inactive.", toEmail);
            return;
        }

        var apiKey = _configuration["Brevo:ApiKey"];
        var senderEmail = _configuration["Brevo:SenderEmail"] ?? "no-reply@ticketsystemtech.app";
        var senderName = _configuration["Brevo:SenderName"] ?? "Ticket System Tech";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Brevo:ApiKey is not configured — email to {Email} with subject '{Subject}' was NOT sent.", toEmail, subject);
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            sender = new { name = senderName, email = senderEmail },
            to = new[] { new { email = toEmail } },
            subject,
            htmlContent = htmlBody
        });

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Brevo email send failed ({Status}) to {Email}: {Body}", response.StatusCode, toEmail, body);
        }
    }
}
