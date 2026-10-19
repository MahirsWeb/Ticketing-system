using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TicketSystemTech.Infrastructure.Persistence;

/// <summary>
/// Every DateTime in this system is stored as UTC (the code writes DateTime.UtcNow), but SQL Server's datetime2
/// carries no kind, so EF hands values back as DateTimeKind.Unspecified. System.Text.Json then serialises them
/// WITHOUT a trailing "Z", and the browser's `new Date(...)` reads the string as local time: every stored time
/// appears shifted by the viewer's UTC offset (1-2 h here), while values just created in the same request (kind
/// Utc, with "Z") are correct — so one screen mixed right and wrong times. Stamping Utc on the way out fixes the
/// whole API at once. On the way in, a Local value is converted to UTC; Unspecified is taken to already be UTC.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => ToStored(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }

    private static DateTime ToStored(DateTime v) => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v;
}

public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v.Value) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}
