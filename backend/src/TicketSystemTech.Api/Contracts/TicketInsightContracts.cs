using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Contracts;

// The "Tickets" tab of the reports: what is old or stuck, how long solving and the first answer take, and the transfers between branches.
// These reports show tickets (number and title), so they follow the isolation of branches: staff see their own branch, Admin all of them (and may narrow
// to one). The exception is the transfers: staff see those that left or entered their branch.

/// <summary>A ticket in a list of the reports: enough to know it and to open it.</summary>
public record TicketBriefDto(
    Guid Id,
    string TicketNumber,
    string Title,
    TicketStatus Status,
    DateTime CreatedAt,
    DateTime? DueDateUtc,
    DateTime? LastActivityUtc, // only where the list is about activity
    int AgeDays
);

/// <summary>A list that may be longer than what is shown: <see cref="Total"/> is all of them, <see cref="Items"/> the first ones (the most striking first).</summary>
public record TicketBriefListDto(int Total, List<TicketBriefDto> Items);

/// <summary>An age group of the active tickets. Key: upTo2Days, upTo7Days, upTo30Days, older.</summary>
public record AgeGroupDto(string Key, int Count, TicketBriefDto? Oldest);

/// <summary>
/// The tickets that need attention now, whatever the period. "Active" is New, Open, InProgress and Resolved (a resolved ticket is not closed yet).
/// </summary>
public record AttentionDto(
    DateTime AsOfUtc,
    int ActiveTotal,
    List<AgeGroupDto> Ages,
    int InactiveDays,
    TicketBriefListDto Inactive, // no message (reply or internal note), audit row, assignment or work session for InactiveDays days
    TicketBriefListDto Unassigned, // nobody is on the ticket
    TicketBriefListDto DueSoon, // the due date is in the next 24 hours
    TicketBriefListDto Overdue // the due date has passed
);

/// <summary>The time to solve (opened to closed, in hours) of the tickets closed in the period: the same tickets and the same definition as the average on the overview.</summary>
public record ResolutionTimeDto(int Count, double AverageHours, double MedianHours, double P90Hours);

/// <summary>
/// The time from the report to the first reply of staff (a message of type Response whose author is an Employee or Admin; system events and internal notes do
/// not count) for the tickets created in the period. A ticket that has no such reply is counted in <see cref="Unanswered"/> and not in the hours.
/// </summary>
public record FirstResponseDto(int Tickets, int Answered, int Unanswered, double AverageHours, double MedianHours, double P90Hours);

/// <summary>From a branch to another. A branch of null: the ticket had none ("unrouted").</summary>
public record TransferEdgeDto(Guid? FromId, string? FromName, Guid ToId, string ToName, int Count);

public record TransferItemDto(Guid TicketId, string TicketNumber, string Title, DateTime AtUtc, Guid? FromId, string? FromName, Guid ToId, string ToName);

public record TransfersDto(int Total, List<TransferEdgeDto> Edges, List<TransferItemDto> Items);
