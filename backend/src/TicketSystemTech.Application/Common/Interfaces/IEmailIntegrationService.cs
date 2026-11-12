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
    Task<EmailConnectionStatus> ConnectAsync(Guid departmentId, Guid connectedByUserId, string code, string redirectUri, string? codeVerifier, CancellationToken ct = default);

    Task DisconnectAsync(Guid departmentId, Guid disconnectedByUserId, CancellationToken ct = default);

    Task<List<InboxMessageSummary>> ListInboxAsync(Guid departmentId, int take, CancellationToken ct = default);

    Task<InboxMessageDetail> GetMessageAsync(Guid departmentId, string messageId, CancellationToken ct = default);

    Task<bool> SetMarkedAsync(Guid departmentId, Guid markedByUserId, string messageId, bool marked, CancellationToken ct = default);

    /// <summary>Downloads the message's attachments from the mailbox, saves them as attachments on the given ticket and marks the message as
    /// converted to it: all in ONE save, so that a failure leaves neither half behind (no files on the ticket for a message that is not marked, and
    /// no "converted" mark on a ticket that never got its files).</summary>
    Task ImportAttachmentsAndMarkConvertedAsync(Guid departmentId, Guid uploadedByUserId, string messageId, Guid ticketId, CancellationToken ct = default);

    /// <summary>The same as <see cref="ImportAttachmentsAndMarkConvertedAsync"/>, but nothing is saved: the files are stored and the rows (attachments and the
    /// "converted" mark) are added to the caller's context, so that the caller saves them in ONE save together with the ticket they belong to. Returns the stored
    /// references of the files, which the caller must delete if its save fails (or is not made).</summary>
    Task<IReadOnlyList<string>> StageAttachmentsAndMarkAsync(Guid departmentId, Guid uploadedByUserId, string messageId, Guid ticketId, CancellationToken ct = default);
}
