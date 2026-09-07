using System.ComponentModel.DataAnnotations;

namespace TicketSystemTech.Api.Contracts;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record LoginResponse(
    bool RequiresPasswordChange,
    string? AccessToken,
    DateTime? ExpiresAtUtc,
    UserSummary? User
);

public record UserSummary(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    Guid? CompanyId,
    string? PhoneNumber,
    bool PhoneNumberPrompted,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? SubBranchId,
    string? SubBranchName
);

public record SetNewPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required] string TemporaryPassword,
    [Required, MinLength(8)] string NewPassword,
    [Required] string ConfirmNewPassword
);

public record VerifyEmailRequest(
    [Required] Guid UserId,
    [Required] string Token
);

public record ForgotPasswordRequest(
    [Required, EmailAddress] string Email
);

public record ResetPasswordRequest(
    [Required] Guid UserId,
    [Required] string Token,
    [Required, MinLength(8)] string NewPassword,
    [Required] string ConfirmNewPassword
);

public record SetPhoneNumberRequest(
    [Required, Phone] string PhoneNumber
);
