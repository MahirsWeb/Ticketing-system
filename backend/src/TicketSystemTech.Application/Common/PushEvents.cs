namespace TicketSystemTech.Application.Common;

/// <summary>
/// The names of the real-time events that are pushed to the browser, written once. The browser listens for each of them by name
/// (<c>NotificationBell.tsx</c>): a name that differs by a letter on either side is not an error anywhere, the screen just stops
/// being told. <c>PushEventsTests</c> holds this list to the one in the browser.
/// </summary>
public static class PushEvents
{
    public const string NewTicket = "newTicket";
    public const string TicketOpened = "ticketOpened";
    public const string TicketAssigned = "ticketAssigned";
    public const string TicketClosed = "ticketClosed";
    public const string NewResponse = "newResponse";
    public const string UserActivatedViaInvite = "userActivatedViaInvite";
    public const string TicketTransferred = "ticketTransferred";
    public const string TicketOverdue = "ticketOverdue";
    public const string TaskAssigned = "taskAssigned";
    public const string TicketUnassigned = "ticketUnassigned";

    public static readonly IReadOnlyList<string> All = new[]
    {
        NewTicket, TicketOpened, TicketAssigned, TicketClosed, NewResponse, UserActivatedViaInvite,
        TicketTransferred, TicketOverdue, TaskAssigned, TicketUnassigned,
    };
}
