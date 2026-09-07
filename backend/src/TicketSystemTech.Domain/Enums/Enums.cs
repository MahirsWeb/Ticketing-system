namespace TicketSystemTech.Domain.Enums;

public enum UserRole
{
    Admin = 0,
    Employee = 1,
    Client = 2
}

public enum TicketStatus
{
    New = 0,
    Open = 1,
    InProgress = 2,
    Resolved = 3,
    Closed = 4
}

public enum TicketSource
{
    Phone = 0,
    Email = 1,
    TicketSystem = 2,
    Other = 3
}

public enum MessageType
{
    Response = 0,
    InternalNote = 1,
    /// <summary>Auto-generated lifecycle entry (opened, assigned, transferred, closed, ...) shown inline in the staff activity feed.</summary>
    SystemEvent = 2
}

/// <summary>Urgency selected by whoever opens the ticket. Ordered so the enum's numeric value sorts most-urgent first.</summary>
public enum TicketPriority
{
    Emergency = 0,
    High = 1,
    Medium = 2,
    Low = 3
}

public enum NotificationType
{
    NewTicket = 0,
    TicketAssigned = 1,
    TicketOpened = 2,
    TicketClosed = 3,
    NewResponse = 4,
    SlaDueSoon = 5,
    UserActivatedViaInvite = 6,
    TicketTransferred = 7,
    TicketOverdue = 8,
    TaskAssigned = 9
}

/// <summary>Lifecycle of a WorkTask, derived from its start/stop timestamps.</summary>
public enum WorkTaskStatus
{
    Pending = 0,
    InProgress = 1,
    Done = 2
}
