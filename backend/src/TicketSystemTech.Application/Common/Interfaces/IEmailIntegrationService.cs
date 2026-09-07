namespace TicketSystemTech.Application.Common.Interfaces;

public record EmailConnectionStatus(bool Connected, string? ConnectedEmail);

public record InboxMessageSummary(
    string MessageId, string Subject, string FromEmail, string FromName,
    string BodyPreview, DateTime ReceivedAtUtc, bool HasAttachments,
    bool IsMarked, Guid? ConvertedTicketId);

public record InboxAttachmentSummary(string AttachmentId, string FileName, string ContentType, long SizeBytes);

public record InboxMessageDetail(
    string MessageId, string Subject, string FromEmail, string FromName,
    string BodyHtml, DateTime ReceivedAtUtc, List<InboxAttachmentSummary> Attachments);

/// <summary>
/// Manages a branch's (Department's) shared mailbox connection. Every staff member assigned to a branch
/// shares the same connection, inbox view, and ticket-candidate marks — this is a team inbox, not a personal one.
/// </summary>
public interface IEmailIntegrationService
{
    Task<EmailConnectionStatus> GetStatusAsync(Guid departmentId, CancellationToken ct = default);

    /// <summary>Exchanges an OAuth authorization code for tokens and stores the connection for this branch.</summary>
    Task<EmailConnectionStatus> ConnectAsync(Guid departmentId, Guid connectedByUserId, string code, string redirectUri, CancellationToken ct = default);

    Task DisconnectAsync(Guid departmentId, CancellationToken ct = default);

    Task<List<InboxMessageSummary>> ListInboxAsync(Guid departmentId, int take, CancellationToken ct = default);

    Task<InboxMessageDetail> GetMessageAsync(Guid departmentId, string messageId, CancellationToken ct = default);

    Task<bool> SetMarkedAsync(Guid departmentId, Guid markedByUserId, string messageId, bool marked, CancellationToken ct = default);

    /// <summary>Downloads the message's attachments from the mailbox and saves them as attachments on the given ticket.</summary>
    Task ImportAttachmentsToTicketAsync(Guid departmentId, Guid uploadedByUserId, string messageId, Guid ticketId, CancellationToken ct = default);

    Task MarkConvertedAsync(Guid departmentId, string messageId, Guid ticketId, CancellationToken ct = default);
}
