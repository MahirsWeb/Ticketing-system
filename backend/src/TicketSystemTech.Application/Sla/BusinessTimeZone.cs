namespace TicketSystemTech.Application.Sla;

/// <summary>
/// The business's own time zone (<c>Sla:TimeZoneId</c>, default Europe/Sarajevo). Timestamps are stored and
/// transported as UTC, but a "day" means a local calendar day: grouping UTC timestamps by their UTC date puts the
/// first (or last) hour or two of a working day into the neighbouring day's bucket.
/// </summary>
public static class BusinessTimeZone
{
    /// <summary>Windows id used when the machine has no ICU time-zone data and so cannot resolve the IANA id of the
    /// shipped default. Deliberately NOT a general fallback: a misspelt zone must be an error, not a silent CET.</summary>
    private const string DefaultIanaId = "Europe/Sarajevo";
    private const string DefaultWindowsId = "Central European Standard Time";

    public static TimeZoneInfo Resolve(string id)
    {
        var candidates = string.Equals(id, DefaultIanaId, StringComparison.OrdinalIgnoreCase)
            ? new[] { id, DefaultWindowsId }
            : new[] { id };

        foreach (var candidate in candidates)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        throw new InvalidOperationException($"Sla:TimeZoneId '{id}' is not a known time zone on this machine.");
    }

    /// <summary>An instant of unknown kind as UTC: Local is converted, Unspecified is taken to already be UTC (how the
    /// database hands values back and how query-string dates arrive).</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>The local calendar date of a UTC instant.</summary>
    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone));
}
