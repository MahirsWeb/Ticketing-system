using System.ComponentModel.DataAnnotations;

namespace TicketSystemTech.Api.Contracts;

public record EmailIntegrationConfigDto(string ClientId);

public record EmailConnectionStatusDto(bool Connected, string? ConnectedEmail);

public record ConnectEmailRequest(
    [Required] string Code,
    [Required] string RedirectUri,
    string? CodeVerifier = null // PKCE: the secret whose hash was sent when the sign-in started; binds the code to this browser.
);

public record SetMarkedRequest(bool Marked);

public record InboxMessageSummaryDto(
    string MessageId, string Subject, string FromEmail, string FromName,
    string BodyPreview, DateTime ReceivedAtUtc, bool HasAttachments,
    bool IsMarked, Guid? ConvertedTicketId);

public record EmailPrefillDto(
    string MessageId, string ClientEmail, string Subject, string BodyHtml,
    DateTime ReceivedAtUtc, bool HasAttachments);
