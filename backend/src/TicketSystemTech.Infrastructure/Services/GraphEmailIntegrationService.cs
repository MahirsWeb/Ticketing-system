using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

public class GraphEmailIntegrationService : IEmailIntegrationService
{
    private const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private const string Scopes = "offline_access Mail.Read User.Read";

    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly IFileStorage _fileStorage;
    private readonly MicrosoftGraphOptions _options;
    private readonly ILogger<GraphEmailIntegrationService> _logger;
    private readonly IDataProtector _protector;
    private readonly IHtmlSanitizerService _html;

    public GraphEmailIntegrationService(
        AppDbContext db, HttpClient http, IFileStorage fileStorage,
        IOptions<MicrosoftGraphOptions> options, ILogger<GraphEmailIntegrationService> logger,
        IDataProtectionProvider dataProtectionProvider, IHtmlSanitizerService html)
    {
        _db = db;
        _http = http;
        _fileStorage = fileStorage;
        _options = options.Value;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector("EmailConnection.OAuthTokens");
        _html = html;
    }

    private string Protect(string value) => _protector.Protect(value);

    // Rows saved before encryption was introduced hold plaintext tokens — treat an Unprotect failure
    // as "this one predates encryption" rather than a hard error, so existing connections (e.g. the
    // one already live in production) keep working and get re-encrypted the next time they're saved.
    private string Unprotect(string value)
    {
        try { return _protector.Unprotect(value); }
        catch (CryptographicException) { return value; }
    }

    public async Task<EmailConnectionStatus> GetStatusAsync(Guid departmentId, CancellationToken ct = default)
    {
        var conn = await _db.EmailConnections.AsNoTracking().FirstOrDefaultAsync(c => c.DepartmentId == departmentId, ct);
        return new EmailConnectionStatus(conn is not null, conn?.ConnectedEmail);
    }

    public async Task<EmailConnectionStatus> ConnectAsync(Guid departmentId, Guid connectedByUserId, string code, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = Scopes,
        };

        JsonElement tokenResponse;
        try
        {
            tokenResponse = await PostFormAsync(TokenEndpoint, form, ct);
        }
        catch (ExternalServiceException ex) when (!ex.Unavailable)
        {
            // Here (the one-time authorization code) a refusal means the sign-in itself was not accepted, e.g. the code
            // was already used or has expired: "the connection expired" would send the user to the wrong place.
            throw new ExternalServiceException("Microsoft 365",
                "Microsoft did not accept the sign-in (it may have expired or already been used). Please start connecting the mailbox again.",
                unavailable: false, ex);
        }

        var accessToken = tokenResponse.GetProperty("access_token").GetString()!;
        var refreshToken = tokenResponse.GetProperty("refresh_token").GetString()!;
        var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{GraphBase}/me?$select=mail,userPrincipalName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var meResponse = await SendAsync(request, ct);
        if (!meResponse.IsSuccessStatusCode) throw RejectedBy(meResponse.StatusCode, null);
        using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStreamAsync(ct));
        var mail = meDoc.RootElement.TryGetProperty("mail", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : meDoc.RootElement.GetProperty("userPrincipalName").GetString();

        var conn = await _db.EmailConnections.FirstOrDefaultAsync(c => c.DepartmentId == departmentId, ct);
        if (conn is null)
        {
            conn = new EmailConnection { DepartmentId = departmentId };
            _db.EmailConnections.Add(conn);
        }
        conn.ConnectedByUserId = connectedByUserId;
        conn.Provider = "Outlook";
        conn.ConnectedEmail = mail ?? "";
        conn.AccessToken = Protect(accessToken);
        conn.RefreshToken = Protect(refreshToken);
        conn.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn);
        conn.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new EmailConnectionStatus(true, conn.ConnectedEmail);
    }

    public async Task DisconnectAsync(Guid departmentId, CancellationToken ct = default)
    {
        var conn = await _db.EmailConnections.FirstOrDefaultAsync(c => c.DepartmentId == departmentId, ct);
        if (conn is not null)
        {
            _db.EmailConnections.Remove(conn);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<InboxMessageSummary>> ListInboxAsync(Guid departmentId, int take, CancellationToken ct = default)
    {
        var conn = await GetValidConnectionAsync(departmentId, ct);
        var url = $"{GraphBase}/me/mailFolders/inbox/messages?$top={Math.Clamp(take, 1, 100)}" +
                  "&$select=id,subject,from,bodyPreview,receivedDateTime,hasAttachments&$orderby=receivedDateTime desc";
        var doc = await GetGraphAsync(conn, url, ct);

        var marks = await _db.EmailTicketMarks.AsNoTracking().Where(m => m.DepartmentId == departmentId).ToDictionaryAsync(m => m.MessageId, ct);

        var results = new List<InboxMessageSummary>();
        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            var from = item.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object
                ? f.GetProperty("emailAddress") : (JsonElement?)null;
            marks.TryGetValue(id, out var mark);

            results.Add(new InboxMessageSummary(
                id,
                item.GetProperty("subject").GetString() ?? "(no subject)",
                from?.GetProperty("address").GetString() ?? "",
                from?.TryGetProperty("name", out var n) == true ? n.GetString() ?? "" : "",
                item.GetProperty("bodyPreview").GetString() ?? "",
                item.GetProperty("receivedDateTime").GetDateTime(),
                item.GetProperty("hasAttachments").GetBoolean(),
                mark?.IsMarked ?? false,
                mark?.ConvertedTicketId
            ));
        }
        return results;
    }

    public async Task<InboxMessageDetail> GetMessageAsync(Guid departmentId, string messageId, CancellationToken ct = default)
    {
        var conn = await GetValidConnectionAsync(departmentId, ct);
        var url = $"{GraphBase}/me/messages/{Uri.EscapeDataString(messageId)}?$select=subject,from,body,receivedDateTime,hasAttachments";
        var doc = await GetGraphAsync(conn, url, ct);
        var root = doc.RootElement;

        var from = root.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object
            ? f.GetProperty("emailAddress") : (JsonElement?)null;
        // Inbound email HTML is attacker-controlled (anyone can mail a branch mailbox) and is rendered raw in
        // the staff UI and can be carried into a ticket, so sanitize it at the source.
        var bodyHtml = _html.Sanitize(root.GetProperty("body").GetProperty("content").GetString());

        var attachments = new List<InboxAttachmentSummary>();
        if (root.GetProperty("hasAttachments").GetBoolean())
        {
            var attUrl = $"{GraphBase}/me/messages/{Uri.EscapeDataString(messageId)}/attachments?$select=id,name,contentType,size";
            var attDoc = await GetGraphAsync(conn, attUrl, ct);
            foreach (var a in attDoc.RootElement.GetProperty("value").EnumerateArray())
            {
                attachments.Add(new InboxAttachmentSummary(
                    a.GetProperty("id").GetString()!,
                    a.GetProperty("name").GetString() ?? "attachment",
                    a.TryGetProperty("contentType", out var ct2) ? ct2.GetString() ?? "application/octet-stream" : "application/octet-stream",
                    a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0
                ));
            }
        }

        return new InboxMessageDetail(
            messageId,
            root.GetProperty("subject").GetString() ?? "(no subject)",
            from?.GetProperty("address").GetString() ?? "",
            from?.TryGetProperty("name", out var n2) == true ? n2.GetString() ?? "" : "",
            bodyHtml,
            root.GetProperty("receivedDateTime").GetDateTime(),
            attachments
        );
    }

    public async Task<bool> SetMarkedAsync(Guid departmentId, Guid markedByUserId, string messageId, bool marked, CancellationToken ct = default)
    {
        var mark = await _db.EmailTicketMarks.FirstOrDefaultAsync(m => m.DepartmentId == departmentId && m.MessageId == messageId, ct);
        if (mark is null)
        {
            mark = new EmailTicketMark { DepartmentId = departmentId, MessageId = messageId };
            _db.EmailTicketMarks.Add(mark);
        }
        mark.IsMarked = marked;
        mark.MarkedByUserId = markedByUserId;
        await _db.SaveChangesAsync(ct);
        return marked;
    }

    public async Task MarkConvertedAsync(Guid departmentId, string messageId, Guid ticketId, CancellationToken ct = default)
    {
        var mark = await _db.EmailTicketMarks.FirstOrDefaultAsync(m => m.DepartmentId == departmentId && m.MessageId == messageId, ct);
        if (mark is null)
        {
            mark = new EmailTicketMark { DepartmentId = departmentId, MessageId = messageId };
            _db.EmailTicketMarks.Add(mark);
        }
        mark.IsMarked = true;
        mark.ConvertedTicketId = ticketId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ImportAttachmentsToTicketAsync(Guid departmentId, Guid uploadedByUserId, string messageId, Guid ticketId, CancellationToken ct = default)
    {
        var conn = await GetValidConnectionAsync(departmentId, ct);
        var url = $"{GraphBase}/me/messages/{Uri.EscapeDataString(messageId)}/attachments";
        var doc = await GetGraphAsync(conn, url, ct);

        foreach (var a in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            if (!a.TryGetProperty("contentBytes", out var contentBytesProp)) continue; // skip non-file (e.g. item) attachments

            var name = a.GetProperty("name").GetString() ?? "attachment";
            // Same allow-list and size cap as direct uploads: an inbound email is the easiest way to get an
            // .html/.svg/.exe in front of staff, so it must not be a way around the rules.
            if (!AttachmentPolicy.IsAllowedExtension(Path.GetExtension(name))) continue;

            var contentType = a.TryGetProperty("contentType", out var ct2) ? ct2.GetString() ?? "application/octet-stream" : "application/octet-stream";
            var bytes = Convert.FromBase64String(contentBytesProp.GetString()!);
            if (bytes.LongLength > AttachmentPolicy.MaxFileSizeBytes) continue;

            using var stream = new MemoryStream(bytes);
            var fileUrl = await _fileStorage.SaveAsync(name, contentType, stream, ct);

            _db.TicketAttachments.Add(new TicketAttachment
            {
                TicketId = ticketId,
                FileName = name,
                FileUrl = fileUrl,
                FileSizeBytes = bytes.Length,
                ContentType = contentType,
                UploadedByUserId = uploadedByUserId
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    // ---------------- helpers ----------------

    private async Task<EmailConnection> GetValidConnectionAsync(Guid departmentId, CancellationToken ct)
    {
        var conn = await _db.EmailConnections.FirstOrDefaultAsync(c => c.DepartmentId == departmentId, ct)
            ?? throw new InvalidOperationException("No mailbox connected for this branch.");

        if (conn.AccessTokenExpiresAtUtc <= DateTime.UtcNow.AddMinutes(1))
        {
            var form = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = Unprotect(conn.RefreshToken),
                ["grant_type"] = "refresh_token",
                ["scope"] = Scopes,
            };
            var tokenResponse = await PostFormAsync(TokenEndpoint, form, ct);
            conn.AccessToken = Protect(tokenResponse.GetProperty("access_token").GetString()!);
            if (tokenResponse.TryGetProperty("refresh_token", out var rt))
                conn.RefreshToken = Protect(rt.GetString()!);
            conn.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokenResponse.GetProperty("expires_in").GetInt32());
            conn.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return conn;
    }

    /// <summary>Reads are repeated a couple of times when Microsoft cannot be reached or says it is busy; they change nothing,
    /// so repeating them is harmless. (The OAuth token POST is deliberately not repeated.)</summary>
    private async Task<JsonDocument> GetGraphAsync(EmailConnection conn, string url, CancellationToken ct)
    {
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await GetGraphOnceAsync(conn, url, ct);
            }
            catch (ExternalServiceException ex) when (ex.Unavailable && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt), ct); // 0.5 s, then 2 s
            }
        }
    }

    private async Task<JsonDocument> GetGraphOnceAsync(EmailConnection conn, string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Unprotect(conn.AccessToken));
        var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Microsoft Graph request failed ({Status}): {Body}", response.StatusCode, body);
            throw RejectedBy(response.StatusCode, body);
        }
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
    }

    private async Task<JsonElement> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        var response = await SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Microsoft token endpoint request failed ({Status}): {Body}", response.StatusCode, body);
            throw RejectedBy(response.StatusCode, body);
        }
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Sends a request to Microsoft, turning "could not reach it / it was too slow / the circuit breaker is open"
    /// into one exception the API can answer with 503 and a clear message. A caller that gave up is left alone.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "Microsoft 365 could not be reached ({Reason})", ex.GetType().Name);
            throw new ExternalServiceException("Microsoft 365",
                "Microsoft 365 could not be reached right now. Please try again in a minute.", unavailable: true, ex);
        }
    }

    /// <summary>What a non-success answer from Microsoft means for the user. Status and the OAuth error code are
    /// examined, but the provider's own text is never passed on.</summary>
    private static ExternalServiceException RejectedBy(System.Net.HttpStatusCode status, string? body)
    {
        if (status == System.Net.HttpStatusCode.TooManyRequests || (int)status >= 500)
            return new ExternalServiceException("Microsoft 365", "Microsoft 365 is busy right now. Please try again in a minute.", unavailable: true);

        var expired = status == System.Net.HttpStatusCode.Unauthorized
            || (body is not null && body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase));
        return expired
            ? new ExternalServiceException("Microsoft 365", "The mailbox connection has expired or was revoked. Disconnect the mailbox and connect it again.", unavailable: false)
            : new ExternalServiceException("Microsoft 365", "Microsoft 365 did not accept the request. If this keeps happening, reconnect the mailbox.", unavailable: false);
    }
}
