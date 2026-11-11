using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>
/// One sign-in session's long-lived half. The browser holds the raw value in an httpOnly cookie (script cannot read it);
/// the database holds only its SHA-256 hash, so a copy of the database cannot be replayed as a session. Each use rotates
/// the token: the presented one is revoked and a fresh one is handed back. A token that is presented AFTER it was rotated
/// away is treated as stolen, and every live token of that person is revoked (they simply sign in again).
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the raw token, Base64. The raw value exists only in the cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set when the token stopped being acceptable: rotated away, signed out, or its family was revoked.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>The hash of the token that replaced this one on rotation; a replaced token coming back is the theft signal.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>The person's security stamp when this token was made. A password change (or anything else that rotates the stamp)
    /// makes every older token fail the comparison at refresh time, so a stolen cookie does not outlive the password it was stolen with.
    /// Null on rows made before this existed: they are refused, so the person signs in once more.</summary>
    public string? SecurityStamp { get; set; }

    /// <summary>When the sign-in this token descends from happened. Rotation copies it, so a session has an absolute end however
    /// often it is renewed (<c>Jwt:SessionMaxDays</c>).</summary>
    public DateTime SessionStartedAtUtc { get; set; }
}
