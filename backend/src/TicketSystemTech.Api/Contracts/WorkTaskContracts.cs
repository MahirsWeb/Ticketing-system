using System.ComponentModel.DataAnnotations;

namespace TicketSystemTech.Api.Contracts;

public record CreateWorkTaskRequest(
    [Required, MaxLength(300)] string Title,
    [Required] string Description,
    [Required] Guid AssignedToUserId
);

public record ReassignWorkTaskRequest(
    [Required] Guid AssignedToUserId
);

public record SetWorkTaskTimeRequest(
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc
);

public record WorkTaskAssignmentLogDto(
    Guid AssignedByUserId,
    string AssignedByName,
    Guid AssignedToUserId,
    string AssignedToName,
    DateTime AssignedAtUtc
);

public record WorkTaskListItem(
    Guid Id,
    string Title,
    string Status,
    Guid DepartmentId,
    string DepartmentName,
    Guid CreatedByUserId,
    string CreatedByName,
    Guid AssignedByUserId,
    string AssignedByName,
    Guid AssignedToUserId,
    string AssignedToName,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    DateTime CreatedAt
);

public record WorkTaskDetailDto(
    Guid Id,
    string Title,
    string Description,
    string Status,
    Guid DepartmentId,
    string DepartmentName,
    Guid CreatedByUserId,
    string CreatedByName,
    Guid AssignedByUserId,
    string AssignedByName,
    Guid AssignedToUserId,
    string AssignedToName,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    DateTime CreatedAt,
    List<WorkTaskAssignmentLogDto> History
);

/// <summary>One block on the per-person Gantt timeline — either a ticket's logged work session or a task's.</summary>
public record GanttEntryDto(
    string Type, // "Ticket" | "WorkTask"
    Guid Id,
    string Title,
    string? SubLabel,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    string Status
);

public record GanttResponse(
    Guid UserId,
    string UserName,
    DateTime Date,
    List<GanttEntryDto> Entries
);

/// <summary>Every Employee's daily timeline in one branch, side by side — Admin's "whole branch" Gantt view.</summary>
public record BranchGanttResponse(
    Guid DepartmentId,
    string DepartmentName,
    DateTime Date,
    List<GanttResponse> Users
);
