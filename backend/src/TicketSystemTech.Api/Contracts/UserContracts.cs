using System.ComponentModel.DataAnnotations;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Contracts;

public record SetUserActiveRequest(
    bool IsActive
);

public record CreateEmployeeRequest(
    [Required] string FirstName,
    [Required] string LastName,
    [Required, EmailAddress] string Email,
    [Required] UserRole Role, // Admin or Employee
    Guid? DepartmentId, // Required for Employee — the branch they belong to. Not applicable to Admin.
    Guid? SubBranchId // Required if the selected branch has sub-branches defined.
);

public record CreateClientRequest(
    [Required] string FirstName,
    [Required] string LastName,
    [Required, EmailAddress] string Email,
    [Required] Guid CompanyId
);

/// <summary>Admin-only edit of an existing account. Email is intentionally excluded — it's the login identity.</summary>
public record UpdateUserRequest(
    [Required] string FirstName,
    [Required] string LastName,
    [Required] UserRole Role,
    Guid? CompanyId, // Required when Role is Client.
    Guid? DepartmentId, // Required when Role is Employee.
    Guid? SubBranchId // Required if the selected branch has sub-branches defined.
);

public record CreatedUserResponse(
    Guid UserId,
    string Email,
    string TemporaryPassword,
    DateTime TemporaryPasswordExpiresAtUtc
);

public record ClientLookupResult(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    Guid? CompanyId,
    string? CompanyName
);

public record UserListItem(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    Guid? CompanyId,
    string? CompanyName,
    string? PhoneNumber,
    bool IsActive,
    bool EmailConfirmed,
    DateTime CreatedAtUtc,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? SubBranchId,
    string? SubBranchName,
    string? ProfilePictureUrl,
    DateTime? LastLoginAtUtc
);
