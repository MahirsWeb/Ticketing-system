namespace TicketSystemTech.Api.Contracts;

// What another application (for example one that records working time) reads from the tickets. It is a deliberately small view: the
// ticket itself, who it is for and who works on it, with their e-mail addresses, which are what the other application knows people by.
// Not in it: the messages and internal notes, the attachments, the internal category, and anything about how long people worked.

/// <summary>A person as the other application knows them: by the e-mail address.</summary>
public sealed record IntegrationPerson(string Email, string Name);

/// <summary>One person on a ticket. <c>IsPrimary</c> is the one the ticket is "assigned to"; the others help.</summary>
public sealed record IntegrationAssignee(string Email, string Name, bool IsPrimary, DateTime AssignedAtUtc);

public sealed record IntegrationTicket(
    Guid Id,
    string TicketNumber,
    string Title,
    string Description, // HTML, cleaned by the application when it was written
    string Status, // New, Open, InProgress, Resolved or Closed
    string? Priority,
    string? Source,
    string? HelpTopic,
    string? Branch,
    string? SubBranch,
    string? Company,
    IntegrationPerson Client,
    DateTime CreatedAt,
    DateTime? OpenedAtUtc,
    DateTime? DueDateUtc,
    DateTime? ClosedAtUtc,
    string? ResolutionSummary,
    string Url, // where a person opens the ticket
    IReadOnlyList<IntegrationAssignee> Assignees
);

/// <summary>"This person was put on this ticket at this time": one item of the feed of new assignments.</summary>
public sealed record IntegrationAssignment(Guid AssignmentId, DateTime AssignedAtUtc, bool IsPrimary, IntegrationPerson Assignee, IntegrationTicket Ticket);

/// <summary>
/// A page of the feed. To go on, ask again with <c>since</c> = <c>NextSince</c> and <c>afterId</c> = <c>NextAfterId</c>; when there was
/// nothing new, both come back unchanged, so asking again and again is safe.
/// </summary>
public sealed record IntegrationAssignmentPage(IReadOnlyList<IntegrationAssignment> Items, DateTime NextSince, Guid? NextAfterId, bool HasMore);

public sealed record IntegrationTicketPage(IReadOnlyList<IntegrationTicket> Items, int Page, int PageSize, int TotalCount);
