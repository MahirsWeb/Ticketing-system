namespace TicketSystemTech.Application.Reporting;

/// <summary>Average, median and 90th percentile of a list of numbers, and how many there were.</summary>
public sealed record Distribution(int Count, double Average, double Median, double P90)
{
    public static Distribution Empty { get; } = new(0, 0, 0, 0);

    public static Distribution Of(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) return Empty;
        var sorted = values.OrderBy(v => v).ToArray();
        return new Distribution(sorted.Length, sorted.Average(), Percentile(sorted, 0.5), Percentile(sorted, 0.9));
    }

    /// <summary>
    /// The percentile by linear interpolation between the two nearest values (the method of Excel's PERCENTILE.INC and of NumPy's default):
    /// the position is p × (n − 1) in the sorted list, and a position between two values is that far from the lower to the higher.
    /// <paramref name="sorted"/> must already be in ascending order. An empty list has no percentile: 0.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var position = Math.Clamp(p, 0, 1) * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
