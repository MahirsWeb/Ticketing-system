namespace TicketSystemTech.Api.Contracts;

// The "Employees" tab of the reports. These are reports about PEOPLE: Admin sees everybody (and may narrow to one branch), an employee only themself.
// Hours are only what was recorded (work sessions on tickets and on tasks), counted in the business time zone, a session by the day it started in; a session
// still running counts up to now; seconds are added up and rounded to two decimals of an hour at the end.

/// <summary>What one person has on their plate now. Tasks have no due date, so "overdue" is only about tickets.</summary>
public record PersonLoadDto(
    Guid UserId,
    string Name,
    string? Branch,
    int OpenTickets, // active tickets (New, Open, InProgress, Resolved) the person is on, as main assignee or as help
    int MainTickets, // of them, the ones they are the main assignee of
    int OverdueTickets, // of them, the ones whose due date has passed
    int OpenTasks // tasks that are not done
);

public record WorkloadDto(DateTime AsOfUtc, List<PersonLoadDto> People);

/// <summary>
/// A person's recorded hours in the period. <see cref="DaysWithoutEntry"/> is null for anybody but an active employee. <see cref="RunningSessions"/> are the sessions that have not ended:
/// they are counted up to now, so the hours are not final.
/// </summary>
public record PersonHoursDto(Guid UserId, string Name, string? Branch, double TicketHours, double TaskHours, double TotalHours, int? DaysWithoutEntry, int RunningSessions);

public record HoursPeopleDto(List<PersonHoursDto> People);

public record DayHoursDto(DateOnly Day, double TicketHours, double TaskHours, double TotalHours);

public record WeekHoursDto(DateOnly WeekStart, double TicketHours, double TaskHours, double TotalHours);

/// <summary>One person's hours by day and by week, and the working days (Monday to Friday, no calendar of holidays) with nothing recorded.</summary>
public record HoursDetailDto(
    Guid UserId,
    string Name,
    double TicketHours,
    double TaskHours,
    double TotalHours,
    List<DayHoursDto> Days,
    List<WeekHoursDto> Weeks,
    List<DateOnly> DaysWithoutEntry,
    bool DaysWithoutEntryApply, // false for anybody but an active employee
    int RunningSessions // not ended: counted up to now, so the hours are not final
);

/// <summary>A topic, category or company in a person's field: the tickets they closed in the period and the hours they recorded on tickets of it.</summary>
public record SpecializationItemDto(Guid Id, string Name, int Closed, double Hours);

public record SpecializationDto(
    Guid UserId,
    string Name,
    int ClosedTotal,
    List<SpecializationItemDto> Topics,
    List<SpecializationItemDto> Categories,
    List<SpecializationItemDto> Companies
);

/// <summary>A company where one person closed most of the tickets of the last 12 months. <see cref="High"/>: 80 % or more. <see cref="Sole"/>: they closed every one of them (100 %): nobody else knows the company.</summary>
public record CompanyDependencyDto(Guid CompanyId, string Company, int Closed, Guid PersonId, string Person, int PersonClosed, double SharePercent, bool High, bool Sole);

public record DependencyDto(int Total, int MinClosed, double MinSharePercent, List<CompanyDependencyDto> Items);
