using System.Globalization;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>How a number is printed in every table the harness writes.</summary>
internal static class Format
{
    /// <summary>A duration at the scale it belongs to, so a table does not print 0.000 or 12345678.9.</summary>
    internal static string Time(double nanoseconds) => nanoseconds switch
    {
        0 => "—",
        < 10 => $"{nanoseconds.ToString("F2", CultureInfo.InvariantCulture)} ns",
        < 1_000 => $"{nanoseconds.ToString("F1", CultureInfo.InvariantCulture)} ns",
        < 1_000_000 => $"{(nanoseconds / 1_000).ToString("F2", CultureInfo.InvariantCulture)} µs",
        < 1_000_000_000 => $"{(nanoseconds / 1_000_000).ToString("F2", CultureInfo.InvariantCulture)} ms",
        _ => $"{(nanoseconds / 1_000_000_000).ToString("F3", CultureInfo.InvariantCulture)} s",
    };

    /// <summary>A mean with its 99.9 % margin, in the mean's unit.</summary>
    internal static string Interval(ResultRow row) => row.IsSupported
        ? $"{Time(row.MeanNs)} ± {Margin(row)}"
        : row.State;

    /// <summary>The margin alone, in the unit of the mean it belongs to.</summary>
    internal static string Margin(ResultRow row)
    {
        var (divisor, unit, digits) = row.MeanNs switch
        {
            < 10 => (1d, "ns", "F2"),
            < 1_000 => (1d, "ns", "F1"),
            < 1_000_000 => (1_000d, "µs", "F2"),
            < 1_000_000_000 => (1_000_000d, "ms", "F2"),
            _ => (1_000_000_000d, "s", "F3"),
        };

        return $"{(row.MarginNs / divisor).ToString(digits, CultureInfo.InvariantCulture)} {unit}";
    }

    internal static string Bytes(double bytes) => bytes switch
    {
        0 => "0 B",
        < 1_024 => $"{bytes.ToString("F0", CultureInfo.InvariantCulture)} B",
        < 1_048_576 => $"{(bytes / 1_024).ToString("F2", CultureInfo.InvariantCulture)} KB",
        _ => $"{(bytes / 1_048_576).ToString("F2", CultureInfo.InvariantCulture)} MB",
    };

    internal static string Percent(double value) => $"±{value.ToString("F1", CultureInfo.InvariantCulture)}%";

    internal static string Ratio(double value) => value.ToString("F2", CultureInfo.InvariantCulture) + "×";

    internal static string Count(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    internal static string Decimal(double value, string format = "F3") => value.ToString(format, CultureInfo.InvariantCulture);
}
