using System.Text.Json;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-09: stored UTC times came back as Unspecified and were serialised without "Z".</summary>
public class UtcDateTimeConverterTests
{
    private static readonly DateTime Stored = new(2026, 10, 19, 8, 30, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Values_read_from_the_database_are_marked_utc()
    {
        var read = (DateTime)new UtcDateTimeConverter().ConvertFromProvider(Stored)!;

        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(Stored.Ticks, read.Ticks); // the instant itself is untouched
    }

    [Fact]
    public void Read_values_serialise_with_a_Z_so_browsers_do_not_treat_them_as_local_time()
    {
        var read = (DateTime)new UtcDateTimeConverter().ConvertFromProvider(Stored)!;

        Assert.EndsWith("Z\"", JsonSerializer.Serialize(read));
    }

    [Fact]
    public void Local_values_are_converted_to_utc_before_they_are_stored()
    {
        var local = new DateTime(2026, 10, 19, 10, 30, 0, DateTimeKind.Local);

        var stored = (DateTime)new UtcDateTimeConverter().ConvertToProvider(local)!;

        Assert.Equal(local.ToUniversalTime(), stored);
    }

    [Fact]
    public void Utc_values_are_stored_unchanged()
    {
        var utc = new DateTime(2026, 10, 19, 8, 30, 0, DateTimeKind.Utc);

        Assert.Equal(utc, (DateTime)new UtcDateTimeConverter().ConvertToProvider(utc)!);
    }

    [Fact]
    public void Nullable_values_follow_the_same_rules_and_keep_null()
    {
        var converter = new NullableUtcDateTimeConverter();

        Assert.Null(converter.ConvertFromProvider(null));
        Assert.Null(converter.ConvertToProvider(null));
        Assert.Equal(DateTimeKind.Utc, ((DateTime)converter.ConvertFromProvider(Stored)!).Kind);
    }
}
