using System.ComponentModel.DataAnnotations;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Contracts;

/// <summary>Ceiling for a rich-text body in characters. Bodies carry pasted screenshots as base64, so this is generous (about
/// 5 MB of text), but it is a limit: the columns are nvarchar(max) and nothing else bounded what one request could store.</summary>
public static class Limits
{
    public const int HtmlBody = 5_000_000;
    public const int ResolutionSummary = 20_000;
}

public record CreateTicketRequest(
    [Required, MaxLength(300)] string Title,
    [Required, MaxLength(Limits.HtmlBody)] string Description,
    [Required] Guid DepartmentId
);

/// <summary>Staff creates + opens a ticket in one step on behalf of a client who reported the issue verbally (e.g. by phone).</summary>
public record CreateTicketOnBehalfRequest(
    [Required, EmailAddress] string ClientEmail,
    [Required, MaxLength(300)] string Title,
    [Required, MaxLength(Limits.HtmlBody)] string Description,
    [Required] TicketSource Source,
    [Required] Guid HelpTopicId,
    [Required] Guid DepartmentId,
    Guid? SubBranchId, // Required if the selected branch has sub-branches defined.
    [Required] Guid SlaPlanId,
    [Required] TicketPriority Priority,
    DateTime? DueDateUtc,
    [Required] Guid AssignedToUserId,
    Guid? CategoryId // Internal-only classification, staff/Admin — never shown to the client.
);

public record OpenTicketRequest(
    [Required] TicketSource Source,
    [Required] Guid HelpTopicId,
    [Required] Guid DepartmentId,
    Guid? SubBranchId, // Required if the selected branch has sub-branches defined.
    [Required] Guid SlaPlanId,
    [Required] TicketPriority Priority,
    DateTime? DueDateUtc,
    [Required] Guid AssignedToUserId,
    Guid? CategoryId // Internal-only classification, staff/Admin — never shown to the client.
);

public record CloseTicketRequest(
    [Required, MaxLength(Limits.ResolutionSummary)] string ResolutionSummary
);

public record AddTicketMessageRequest(
    [Required] MessageType Type,
    [Required, MaxLength(Limits.HtmlBody)] string BodyHtml
);

public record ChangeTicketStatusRequest(
    [Required] TicketStatus Status
);

public record AddTicketAssigneeRequest(
    [Required] Guid UserId
);

public record SetTicketWorkTimeRequest(
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc
);

public record TicketListItem(
    Guid Id,
    string TicketNumber,
    string Title,
    string Status,
    string? Priority,
    string CompanyName,
    Guid ClientId,
    string ClientName,
    string? AssignedToName,
    DateTime CreatedAt,
    DateTime? DueDateUtc,
    DateTime? ClosedAtUtc,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? SubBranchId,
    string? SubBranchName
);

public record TransferTicketBranchRequest(
    [Required] Guid DepartmentId
);

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

public record TicketCountsDto(int Total, int New, int Opened, int Closed, int Mine);

public record TicketAttachmentDto(Guid Id, string FileName, string FileUrl, long FileSizeBytes, string ContentType, DateTime CreatedAt);

public record TicketMessageDto(
    Guid Id,
    string Type,
    string BodyHtml,
    Guid AuthorId,
    string AuthorName,
    DateTime CreatedAt,
    List<TicketAttachmentDto> Attachments
);

public record TicketAssigneeDto(Guid UserId, string Name);

public record TicketDetailDto(
    Guid Id,
    string TicketNumber,
    string Title,
    string Description,
    string Status,
    Guid ClientId,
    string ClientName,
    string? ClientEmail,
    string? ClientPhoneNumber,
    Guid CompanyId,
    string CompanyName,
    string? Source,
    Guid? HelpTopicId,
    string? HelpTopicName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? SubBranchId,
    string? SubBranchName,
    Guid? SlaPlanId,
    string? SlaPlanName,
    string? Priority,
    DateTime? DueDateUtc,
    Guid? CategoryId, // Both null when the requester is a Client — internal-only field.
    string? CategoryName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    List<TicketAssigneeDto> Assignees,
    DateTime? WorkStartedAtUtc,
    DateTime? WorkEndedAtUtc,
    DateTime? OpenedAtUtc,
    DateTime? ClosedAtUtc,
    string? ResolutionSummary,
    DateTime CreatedAt,
    List<TicketAttachmentDto> Attachments,
    List<TicketMessageDto> Messages
);
