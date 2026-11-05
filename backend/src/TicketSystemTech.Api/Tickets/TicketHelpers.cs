using System.Net;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Api.Tickets;

/// <summary>Small things several ticket services and the controller share.</summary>
public static class TicketHelpers
{
    // Names/branches are user-controlled text interpolated into HTML the server itself builds
    // (system-event feed entries), so they must be encoded.
    public static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    public static string FullName(ApplicationUser u) => $"{H(u.FirstName)} {H(u.LastName)}";

    /// <summary>A date without a zone means UTC (the same rule as work sessions and reports), not the host's zone.</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
