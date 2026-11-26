namespace TicketSystemTech.Api.Contracts;

// The figures of the "companies" side of the overview. Two groups of tickets are counted, and each figure says which:
//   - created in the period (CreatedAt): what came in, and how it stands now: the totals, the states, the topics, the categories, the branches, who reported it;
//   - closed in the period (ClosedAtUtc, status Closed): what got done, and by whom ("who solves it").
// An id that is null stands for "none": a ticket that has not been given a topic, a category, a branch or a sub-branch yet (they are filled in when staff
// open it), so that the rows of a breakdown add up to the total.

/// <summary>One company in a ranking. The figures are for the tickets created in the period; the resolution time and the SLA are for those of them that are closed by now.</summary>
public record CompanyRowDto(
    Guid CompanyId,
    string Name,
    int Total,
    int New,
    int Open,
    int Closed,
    double AvgResolutionHours,
    double? SlaComplianceRate, // null when none of the closed tickets has a due date: "no data" is not 100 %
    int PreviousTotal, // tickets created in the period of the same length that came right before this one
    double SharePercent // of all the tickets created in the period, by every company
);

public record TopCompaniesDto(List<CompanyRowDto> Items, int TotalTickets, int TopTickets, int TotalCompanies);

public record CompanyListPageDto(List<CompanyRowDto> Items, int Page, int PageSize, int TotalCount, int TotalTickets);

/// <summary>The cards of one company, or of one person of it.</summary>
public record CompanySummaryDto(int Total, int New, int Open, int Closed, double AvgResolutionHours, double? SlaComplianceRate, int PreviousTotal);

public record CompanyClientStatDto(Guid ClientId, string Name, int Total, int Open, int Closed);

public record BreakdownItemDto(Guid? Id, string? Name, int Count, bool IsActive);

public record SubBranchCountDto(Guid SubBranchId, string Name, int Count);

/// <summary>A branch with its count, and the sub-branches that share it. <c>NoSubBranchCount</c> is what has none (a branch without sub-branches has all of its tickets there).</summary>
public record BranchCountDto(Guid? DepartmentId, string? Name, int Count, int NoSubBranchCount, List<SubBranchCountDto> SubBranches);

public record CloserBranchDto(Guid? DepartmentId, string? Name, int Count);

public record CloserStatDto(Guid UserId, string Name, int ClosedCount, List<CloserBranchDto> Branches);

/// <summary>Who closed the tickets that were closed in the period, and in which branches those tickets were.</summary>
public record ClosedByDto(List<CloserStatDto> People, List<BranchCountDto> Branches, int TotalClosed);

// ---- the extra statistics. Each says which tickets it counts.

/// <summary>The tickets that are not closed NOW, by how long ago they were created, whatever the period (the company or person chosen decides whose; with none, the ten companies with the most tickets in the period). The oldest one is given as its age and its company, never as a ticket.</summary>
public record OpenAgeDto(int Total, int UpTo2Days, int UpTo7Days, int UpTo30Days, int Older, int OldestDays, string? OldestCompany);

/// <summary>The time written down on tickets: the work sessions that STARTED in the period, on the tickets of the company (or person) chosen. A session still running has no end yet and is counted apart, not in the hours.</summary>
public record TimeSpentDto(double TotalHours, int Sessions, int Tickets, double AvgHoursPerTicket, int RunningSessions);

/// <summary>Of the tickets created in the period: replies in the conversation (client-visible responses, not internal notes or system events) per ticket, and the time from the report to the moment staff picked the ticket up.</summary>
public record ConversationDto(int Tickets, double AvgMessagesPerTicket, int PickedUp, double AvgHoursToPickup, int NotPickedUp);

/// <summary>When tickets of the period were created, by weekday (Monday first) and by hour of the day, in the business time zone.</summary>
public record RhythmDto(int Total, List<int> ByWeekday, List<int> ByHour, string TimeZone);

/// <summary>The same person reporting the same topic more than once in the period.</summary>
public record RepeatedTopicDto(Guid ClientId, string ClientName, Guid TopicId, string TopicName, int Count);

public record RepeatedTopicsDto(int Groups, int Tickets, List<RepeatedTopicDto> Items);

/// <summary>A company that had tickets in the period right before and has none in this one.</summary>
public record QuietCompanyDto(Guid CompanyId, string Name, int PreviousTotal, DateTime? LastTicketAtUtc);

public record QuietCompaniesDto(int Total, List<QuietCompanyDto> Items);
