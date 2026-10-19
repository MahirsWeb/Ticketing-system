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

/// <summary>One page of accounts for the Users screen. The two totals ignore the search text, so the tab labels
/// ("Clients (2898)") keep showing how many accounts exist while the table shows only the matches.</summary>
public record UserPageDto(List<UserListItem> Items, int Page, int PageSize, int TotalCount, int ClientCount, int StaffCount);

public record CreatedUserResponse(
    Guid UserId,
    string Email,
    string? TemporaryPassword, // Null when the caller must not see it (a non-admin resetting a client): it is only emailed.
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

/// <summary>
/// The multipart form of a profile picture upload: one file in a field named "file". It is a class, and not a bare
/// <c>[FromForm] IFormFile</c> parameter, because Swashbuckle 10 refuses to describe the latter, which breaks the whole Swagger
/// document; and without <c>[FromForm]</c> a request with the wrong content type is answered 415 before it is even asked to sign in.
/// </summary>
public sealed class ProfilePictureForm
{
    public IFormFile? File { get; set; }
}
