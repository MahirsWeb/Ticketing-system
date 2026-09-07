using Microsoft.AspNetCore.Identity;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Infrastructure.Identity;

/// <summary>
/// Identity user extended with the fields the ticket system needs beyond the Identity defaults.
/// EmailConfirmed (Identity) doubles as our "email verified" flag; PasswordHash (Identity) is reused
/// for the short-lived temporary password issued at onboarding.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    /// <summary>Set only for Client users — the external company they belong to.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Set only for Employee users — the single branch (grana) they belong to. Admin is not scoped to a branch.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Required when their branch has sub-branches defined; picks which one this staff member belongs to.</summary>
    public Guid? SubBranchId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>True right after an admin/consultant creates the account; forces the user to set their own password on first login.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Expiry of the one-time temporary password (~5 minutes after creation/regeneration).</summary>
    public DateTime? TemporaryPasswordExpiresAtUtc { get; set; }

    /// <summary>Whether the client has been prompted for / provided a phone number yet (banner dismissal).</summary>
    public bool PhoneNumberPrompted { get; set; }

    /// <summary>Optional avatar uploaded by the user (or an admin on their behalf). Null shows a default placeholder.</summary>
    public string? ProfilePictureUrl { get; set; }
}
