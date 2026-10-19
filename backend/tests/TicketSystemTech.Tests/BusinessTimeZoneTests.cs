using TicketSystemTech.Application.Sla;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-32: report days were UTC days, and query-string dates arrived with an unreliable kind.</summary>
public class BusinessTimeZoneTests
{
    private static readonly TimeZoneInfo Sarajevo = BusinessTimeZone.Resolve("Europe/Sarajevo");

    [Fact]
    public void A_late_evening_utc_time_belongs_to_the_next_local_day()
    {
        // 23:30 UTC on 14 Oct is 01:30 on the 15th in Sarajevo (UTC+2 in summer time).
        var utc = new DateTime(2026, 10, 14, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 15), BusinessTimeZone.LocalDate(utc, Sarajevo));
    }

    [Fact]
    public void An_early_morning_local_time_belongs_to_the_previous_utc_day()
    {
        // 00:30 local on 1 Nov is 23:30 UTC on 31 Oct: grouped by UTC it would land in October.
        var utc = new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 11, 1), BusinessTimeZone.LocalDate(utc, Sarajevo));
    }

    [Fact]
    public void Winter_time_uses_the_other_offset()
    {
        // In January Sarajevo is UTC+1.
        var utc = new DateTime(2027, 1, 10, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2027, 1, 11), BusinessTimeZone.LocalDate(utc, Sarajevo));
        Assert.Equal(new DateOnly(2027, 1, 10), BusinessTimeZone.LocalDate(utc.AddHours(-1).AddMinutes(-1), Sarajevo));
    }

    [Fact]
    public void Unspecified_values_are_taken_as_utc_as_the_database_returns_them()
    {
        var unspecified = new DateTime(2026, 10, 14, 23, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(DateTimeKind.Utc, BusinessTimeZone.AsUtc(unspecified).Kind);
        Assert.Equal(unspecified.Ticks, BusinessTimeZone.AsUtc(unspecified).Ticks);
    }

    [Fact]
    public void Local_values_are_converted_to_the_same_instant_in_utc()
    {
        var local = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Local);

        var utc = BusinessTimeZone.AsUtc(local);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(local.ToUniversalTime(), utc);
    }

    [Fact]
    public void Utc_values_are_left_alone()
    {
        var utc = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(utc, BusinessTimeZone.AsUtc(utc));
    }

    [Fact]
    public void An_unknown_zone_is_a_clear_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BusinessTimeZone.Resolve("Mars/Olympus_Mons"));

        Assert.Contains("Mars/Olympus_Mons", ex.Message);
    }
}
