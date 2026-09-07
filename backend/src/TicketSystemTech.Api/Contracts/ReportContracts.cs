namespace TicketSystemTech.Api.Contracts;

public record ReportSummaryDto(
    int TotalNew,
    int TotalOpen,
    int TotalClosed,
    double AvgResolutionHours,
    double SlaComplianceRate
);

public record TimeSeriesPointDto(DateOnly Date, int Opened, int Closed);

public record LeaderboardEntryDto(Guid UserId, string Name, int ClosedCount);

public record BranchBreakdownEntryDto(Guid DepartmentId, string DepartmentName, int TotalNew, int TotalOpen, int TotalClosed);

public record PeriodComparisonPointDto(string Label, DateTime PeriodStart, int Count);

public record TopIssueDto(string HelpTopicName, int Count);

public record AiInsightsRequest(DateTime From, DateTime To, Guid? CompanyId, Guid? AgentId, Guid? DepartmentId, Guid? SubBranchId);

public record AiInsightsResponseDto(string Summary);
