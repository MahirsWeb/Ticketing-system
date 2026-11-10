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
}
