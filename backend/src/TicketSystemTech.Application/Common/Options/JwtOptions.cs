namespace TicketSystemTech.Application.Common.Options;

public class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "TicketSystemTech";
    public string Audience { get; set; } = "TicketSystemTech.Client";
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>How long a sign-in survives without being used. Every refresh rotates the token and starts a fresh window,
    /// so an active person is never signed out; an abandoned session dies after this many days.</summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>The longest a sign-in can be renewed without typing the password again, however active the person is.</summary>
    public int SessionMaxDays { get; set; } = 30;

    /// <summary>A refresh token that was spent this many seconds ago and is presented again is a second tab or request that raced the
    /// first, not a thief: it gets its own fresh token instead of revoking the whole session.</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 20;
}
