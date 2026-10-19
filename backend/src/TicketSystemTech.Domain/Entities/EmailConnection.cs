using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>An OAuth connection to a branch's shared mailbox (e.g. servis@firma.ba), used to triage inbound emails into tickets. Visible to every staff member in that branch.</summary>
public class EmailConnection : BaseEntity
{
    public Guid DepartmentId { get; set; }

    /// <summary>Staff member who performed the OAuth connect, kept for audit purposes only.</summary>
    public Guid ConnectedByUserId { get; set; }

    public string Provider { get; set; } = "Outlook";
    public string ConnectedEmail { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Two requests refreshing the same expiring OAuth token at once would otherwise overwrite each other's
    /// freshly issued tokens (a refresh token can be single-use). The loser now fails instead of clobbering.</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
