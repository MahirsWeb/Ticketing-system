namespace TicketSystemTech.Application.Common.Options;

public class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "TicketSystemTech";
    public string Audience { get; set; } = "TicketSystemTech.Client";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}
