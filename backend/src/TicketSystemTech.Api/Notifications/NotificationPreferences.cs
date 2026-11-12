using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Notifications;

/// <summary>
/// Which e-mails about tickets and tasks a person can choose not to get. The choice is about the e-mail only: the line in the notification list and the
/// real-time push always arrive, and the e-mails about the account itself (an invitation, a password, a lock, a branch's mailbox) are never optional.
/// The choice is kept on the account as the names of the kinds of event that are switched off, so that a notice is filtered where the recipient is already
/// in hand, without a query of its own.
/// </summary>
public static class NotificationPreferences
{
    private static readonly NotificationType[] Staff =
    {
        NotificationType.NewTicket, NotificationType.TicketAssigned, NotificationType.TicketTransferred,
        NotificationType.NewResponse, NotificationType.TaskAssigned, NotificationType.TicketOverdue
    };

    private static readonly NotificationType[] Client =
    {
        NotificationType.TicketOpened, NotificationType.TicketClosed, NotificationType.NewResponse
    };

    /// <summary>The kinds of e-mail this role is ever sent, which are the ones it may switch off.</summary>
    public static IReadOnlyList<NotificationType> EmailTypesFor(UserRole role) => role == UserRole.Client ? Client : Staff;

    public static bool IsMuted(string? muted, NotificationType type) =>
        Parse(muted).Contains(type);

    public static HashSet<NotificationType> Parse(string? muted)
    {
        var set = new HashSet<NotificationType>();
        if (string.IsNullOrWhiteSpace(muted)) return set;
        foreach (var part in muted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<NotificationType>(part, ignoreCase: false, out var type) && Enum.IsDefined(type)) set.Add(type);
        return set;
    }

    public static string Serialize(IEnumerable<NotificationType> muted) =>
        string.Join(',', muted.Distinct().OrderBy(t => (int)t).Select(t => t.ToString()));
}
