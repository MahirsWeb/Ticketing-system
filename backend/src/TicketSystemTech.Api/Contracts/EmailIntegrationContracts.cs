using System.ComponentModel.DataAnnotations;

namespace TicketSystemTech.Api.Contracts;

public record EmailIntegrationConfigDto(string ClientId);

public record EmailConnectionStatusDto(bool Connected, string? ConnectedEmail);

public record ConnectEmailRequest(
    [Required] string Code,
    [Required] string RedirectUri
);

public record SetMarkedRequest(bool Marked);

public record InboxMessageSummaryDto(
    string MessageId, string Subject, string FromEmail, string FromName,
    string BodyPreview, DateTime ReceivedAtUtc, bool HasAttachments,
    bool IsMarked, Guid? ConvertedTicketId);

public record EmailPrefillDto(
    string MessageId, string ClientEmail, string Subject, string BodyHtml,
    DateTime ReceivedAtUtc, bool HasAttachments);
