using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Contracts;

// The card of a ticket (its whole life on one line of time) and the monthly report of a company. Both are documents: they are the state at the moment they are
// made, and what is on the screen is what is in the xlsx.

/// <summary>
/// The kinds of event on the card: Created, Opened, Assigned, Unassigned, StatusChanged, Transferred, Closed, Reopened, StaffReply, ClientReply, InternalNote,
/// WorkSession. Replies and notes are only counted and timed: their text is not on the card.
/// </summary>
public record TicketCardEventDto(
    DateTime AtUtc,
    string Kind,
    string? Person, // who did it or who it is about
    string? From, // StatusChanged: the status it left. Transferred: the branch it left (null: it had none)
    string? To, // StatusChanged: the status it went to. Transferred: the branch it went to. Opened: nothing
    long? DurationSeconds, // WorkSession
    bool Running // WorkSession still going on: the duration is up to the moment the card was made
);

/// <summary>A stretch in one status. A status of null: the trail does not say, and it is not guessed. ToUtc null: it still is.</summary>
public record StatusSpanDto(string? Status, DateTime FromUtc, DateTime? ToUtc, long Seconds);

public record StatusTotalDto(string Status, long Seconds);

public record TicketCardHeaderDto(
    Guid Id,
    string TicketNumber,
    string Title,
    TicketStatus Status,
    string Company,
    string Client,
    string? Branch,
    string? Assignee,
    string? Priority,
    DateTime CreatedAt,
    DateTime? OpenedAtUtc,
    DateTime? ClosedAtUtc,
    double? ResolutionHours,
    DateTime? DueDateUtc,
    bool? SlaMet, // null: not closed, or no due date
    int StaffReplies,
    int ClientReplies,
    int InternalNotes,
    double WorkHours
);

/// <summary>
/// The card, for staff only (a client is refused). <see cref="TrailComplete"/> is false when some time could not be put in a status.
/// </summary>
public record TicketCardDto(
    DateTime AsOfUtc,
    TicketCardHeaderDto Header,
    List<TicketCardEventDto> Events,
    List<StatusSpanDto> Spans,
    List<StatusTotalDto> StatusTotals,
    long UnknownSeconds,
    bool TrailComplete
);

// ---------------------------------------------------------------- the monthly report of a company

public record CompanyMonthlyHeaderDto(
    Guid CompanyId,
    string Company,
    int Year,
    int Month,
    string Audience, // internal | client
    bool IncludesHours,
    DateTime MadeAtUtc,
    string MadeBy,
    string ListScope // "all" or the name of the branch the list of tickets is limited to (an employee gets only the tickets of their own branch)
);

public record StatusCountDto(string Status, int Count);

public record NamedCountDto(Guid? Id, string Name, int Count); // an id of null stands for "none"

public record CompanyMonthlyTicketDto(Guid Id, string TicketNumber, string Title, TicketStatus Status, DateTime CreatedAt, DateTime? ClosedAtUtc, double? ResolutionHours);

/// <summary>
/// One company in one calendar month of the business time zone. The figures are for ALL branches (also for an employee); only the list of tickets is limited to the
/// branch of an employee. The client version has no internal category, no hours unless asked for, no internal notes, and no names of staff.
/// </summary>
public record CompanyMonthlyDto(
    CompanyMonthlyHeaderDto Header,
    int Created, // tickets created in the month
    int Closed, // tickets closed in the month
    List<StatusCountDto> ByStatus, // of the tickets created in the month, as they stand now
    List<NamedCountDto> ByTopic,
    List<NamedCountDto>? ByCategory, // internal only
    int ResolutionCount,
    double AverageResolutionHours, // of the tickets closed in the month, opened to closed: the same as the overview
    double MedianResolutionHours,
    double? SlaComplianceRate, // null when none of the tickets closed in the month has a due date
    int SlaCounted,
    double? WorkHours, // null when not included
    List<CompanyMonthlyTicketDto> Tickets, // created or closed in the month
    int TicketsTotal,
    bool TicketsTruncated
);
