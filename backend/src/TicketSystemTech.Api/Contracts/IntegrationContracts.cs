using System.Text.Json.Serialization;

namespace TicketSystemTech.Api.Contracts;

// What another application (for example one that records working time) reads from the tickets. It is a deliberately small view: the
// ticket itself, who it is for and who works on it, with their e-mail addresses, which are what the other application knows people by.
// Not in it: the messages and internal notes, the attachments, the internal category. How long people worked is in it only when it is asked
// for (includeWork=true): who works how long on what is a thing the other application has to decide it needs.

/// <summary>A person as the other application knows them: by the e-mail address.</summary>
public sealed record IntegrationPerson(string Email, string Name);

/// <summary>One person on a ticket. <c>IsPrimary</c> is the one the ticket is "assigned to"; the others help.</summary>
public sealed record IntegrationAssignee(string Email, string Name, bool IsPrimary, DateTime AssignedAtUtc);

/// <summary>One stretch of work on a ticket, as it was logged. <c>Id</c> is the same in every answer for as long as the session exists. <c>EndedAtUtc</c> is null while the person is still working.</summary>
public sealed record IntegrationWorkSession(Guid Id, IntegrationPerson Person, DateTime StartedAtUtc, DateTime? EndedAtUtc);

/// <summary>
/// One stretch of work of one person, on a ticket (<c>Kind</c> = Ticket) or on a task, an informal work item with no client (Task). <c>Id</c> does not
/// change while the entry exists; an entry that was there and is no longer was deleted (or its start was moved out of the period asked for).
/// <c>WorkDate</c> is the day it started on in the business's own time zone (yyyy-MM-dd): the day it counts for. While it is running (<c>IsRunning</c>)
/// it has no end and no duration yet. <c>Minutes</c> is the duration rounded to the nearest whole minute, <c>Seconds</c> is exact.
/// </summary>
public sealed record IntegrationTimeEntry(
    Guid Id, string Kind, Guid? TicketId, string? TicketNumber, Guid? TaskId, string? TaskTitle, IntegrationPerson Person,
    string WorkDate, DateTime StartedAtUtc, DateTime? EndedAtUtc, bool IsRunning, long? Seconds, int? Minutes);

public sealed record IntegrationTimeEntryPage(IReadOnlyList<IntegrationTimeEntry> Items, int Page, int PageSize, int TotalCount);

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
    IReadOnlyList<IntegrationAssignee> Assignees,
    // Only with includeWork=true; left out of the answer otherwise, so the answer that was agreed does not grow.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<IntegrationWorkSession>? WorkSessions = null,
    // The sum of all the sessions of all the people; a session that has not ended counts up to the moment of the answer.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? WorkedSeconds = null
);

/// <summary>"This person was put on this ticket at this time": one item of the feed of new assignments.</summary>
public sealed record IntegrationAssignment(Guid AssignmentId, DateTime AssignedAtUtc, bool IsPrimary, IntegrationPerson Assignee, IntegrationTicket Ticket);

/// <summary>
/// Something that happened to a ticket: it was opened, somebody was put on it or taken off, its status changed, it was closed, reopened or
/// moved to another branch. <c>Type</c> is one of Opened, AssigneeAdded, AssigneeRemoved, StatusChanged, Closed, Reopened, Transferred.
/// <c>Person</c> is who it is about (the one put on or taken off), <c>FromStatus</c>/<c>ToStatus</c> are there for a change of status; both are
/// null where they do not apply. <c>Ticket</c> is the ticket as it is NOW (not as it was then), or null if it no longer exists.
/// </summary>
public sealed record IntegrationEvent(
    Guid EventId, DateTime OccurredAtUtc, string Type, Guid TicketId, IntegrationPerson? Person, string? FromStatus, string? ToStatus, IntegrationTicket? Ticket);

/// <summary>A page of the feed of events; it is read exactly like <see cref="IntegrationAssignmentPage"/>.</summary>
public sealed record IntegrationEventPage(IReadOnlyList<IntegrationEvent> Items, DateTime NextSince, Guid? NextAfterId, bool HasMore);

/// <summary>
/// A page of the feed. To go on, ask again with <c>since</c> = <c>NextSince</c> and <c>afterId</c> = <c>NextAfterId</c>; when there was
/// nothing new, both come back unchanged, so asking again and again is safe.
/// </summary>
public sealed record IntegrationAssignmentPage(IReadOnlyList<IntegrationAssignment> Items, DateTime NextSince, Guid? NextAfterId, bool HasMore);

public sealed record IntegrationTicketPage(IReadOnlyList<IntegrationTicket> Items, int Page, int PageSize, int TotalCount);
