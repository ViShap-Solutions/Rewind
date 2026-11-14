using System.Globalization;
using System.Text;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// The pieces every chart is drawn from. SVG is written directly rather than through a charting package, so a
/// committed chart has no dependency that can stop rendering later, and a README can reference the file as it stands.
/// </summary>
/// <remarks>
/// Each chart paints an explicit light background and dark ink: a viewer embedding it as an image gets no stylesheet
/// of ours, so a chart that relied on the page's colours would be unreadable in one of the two themes.
/// </remarks>
internal static class Svg
{
    internal const string Ink = "#1f2933";
    internal const string Muted = "#6b7280";
    internal const string Grid = "#e3e7ec";
    internal const string Background = "#ffffff";

    /// <summary>Series colours, distinguishable from one another and for the common forms of colour blindness.</summary>
    internal static readonly string[] Series =
    [
        "#2f6f9f", "#c2702a", "#3f8458", "#8a5fa8", "#b23a52", "#7a7f35", "#3b8fa3", "#6b6f78",
    ];

    internal static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    internal static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A value with as many digits as it deserves and no more, for an axis or a bar label.</summary>
    internal static string Compact(double value) => Math.Abs(value) switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.##", CultureInfo.InvariantCulture) + " G",
        >= 1_000_000 => (value / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + " M",
        >= 1_000 => (value / 1_000).ToString("0.##", CultureInfo.InvariantCulture) + " k",
        >= 10 => value.ToString("0.#", CultureInfo.InvariantCulture),
        _ => value.ToString("0.##", CultureInfo.InvariantCulture),
    };

    internal static void Text(
        StringBuilder svg, double x, double y, string text, string fill = Ink, int size = 12, string anchor = "start", bool bold = false)
    {
        svg.Append(CultureInfo.InvariantCulture, $"""<text x="{Number(x)}" y="{Number(y)}" fill="{fill}" font-size="{size}" """);
        svg.Append(CultureInfo.InvariantCulture, $"""text-anchor="{anchor}" font-family="Segoe UI, Helvetica, Arial, sans-serif" """);
        svg.Append(bold ? """font-weight="600">""" : ">");
        svg.Append(Escape(text)).Append("</text>\n");
    }

    internal static void Rect(StringBuilder svg, double x, double y, double width, double height, string fill, double radius = 0)
    {
        svg.Append(CultureInfo.InvariantCulture, $"""<rect x="{Number(x)}" y="{Number(y)}" width="{Number(width)}" """);
        svg.Append(CultureInfo.InvariantCulture, $"""height="{Number(height)}" rx="{Number(radius)}" fill="{fill}" />""").Append('\n');
    }

    internal static void Line(StringBuilder svg, double x1, double y1, double x2, double y2, string stroke, double width = 1, bool dashed = false)
    {
        svg.Append(CultureInfo.InvariantCulture, $"""<line x1="{Number(x1)}" y1="{Number(y1)}" x2="{Number(x2)}" y2="{Number(y2)}" """);
        svg.Append(CultureInfo.InvariantCulture, $"""stroke="{stroke}" stroke-width="{Number(width)}"{(dashed ? " stroke-dasharray=\"4 3\"" : string.Empty)} />""").Append('\n');
    }

    internal static StringBuilder Open(double width, double height, string title, string subtitle)
    {
        var svg = new StringBuilder();

        svg.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{Number(width)}" height="{Number(height)}" """);
        svg.Append(CultureInfo.InvariantCulture, $"""viewBox="0 0 {Number(width)} {Number(height)}" role="img">""").Append('\n');

        svg.Append("<title>").Append(Escape(title)).Append("</title>\n");
        svg.Append("<desc>").Append(Escape(subtitle)).Append("</desc>\n");

        Rect(svg, 0, 0, width, height, Background);
        Text(svg, 20, 30, title, Ink, 16, bold: true);
        Text(svg, 20, 50, subtitle, Muted, 11);

        return svg;
    }

    internal static string Close(StringBuilder svg) => svg.Append("</svg>\n").ToString();

    /// <summary>
    /// A stable colour per label, so one library or method keeps its colour across every chart and every run. The hash
    /// is computed here rather than taken from <c>string.GetHashCode</c>, which .NET randomizes per process.
    /// </summary>
    internal static int Colour(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return 0;
        }

        uint hash = 2166136261;

        foreach (char character in label)
        {
            hash = (hash ^ character) * 16777619;
        }

        return (int)(hash % (uint)Series.Length);
    }
}

/// <summary>
/// A horizontal bar chart, optionally grouped. Bars run sideways because a label is a method and its parameters,
/// which does not fit under a vertical bar. Every bar may carry its 99.9 % interval as a whisker, so two bars whose
/// whiskers overlap read as no measurable difference rather than as a ranking.
/// </summary>
internal sealed class BarChart(string title, string unit, bool lowerIsBetter, double labelWidth = 280)
{
    private const double Width = 980;
    private const double Right = 110;
    private const double Top = 72;
    private const double RowHeight = 22;
    private const double GroupGap = 14;

    private readonly List<(string Group, string Label, double Value, int Colour, double Error, bool Floor)> _bars = [];

    /// <summary>
    /// Adds a bar. <paramref name="error"/> is the half-width of the cell's interval in the bar's unit;
    /// <paramref name="floor"/> draws the bar hatched, for a reference row that does not do the job measured.
    /// </summary>
    internal void Add(string label, double value, int colour = 0, double error = 0, string group = "", bool floor = false) =>
        _bars.Add((group, label, value, colour, error, floor));

    internal int Count => _bars.Count;

    internal string Render()
    {
        if (_bars.Count == 0)
        {
            return Svg.Close(Svg.Open(Width, 120, title, "no data"));
        }

        var groups = _bars.Select(bar => bar.Group).Distinct().ToList();
        bool grouped = groups.Count > 1 || groups[0].Length > 0;
        double height = Top + (_bars.Count * RowHeight) + (grouped ? groups.Count * (RowHeight + GroupGap) : 0) + 48;
        double plot = Width - labelWidth - Right;
        double max = _bars.Count > 0 ? _bars.Max(bar => bar.Value + bar.Error) : 1;

        if (max <= 0)
        {
            max = 1;
        }

        var subtitle = $"{unit} — {(lowerIsBetter ? "lower is better" : "higher is better")}"
                       + (_bars.Any(bar => bar.Error > 0) ? " — whiskers: 99.9 % confidence interval" : string.Empty);
        var svg = Svg.Open(Width, height, title, subtitle);
        svg.Append("""<defs><pattern id="floor" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="6" height="6" fill="#d7dbe0"/><line x1="0" y1="0" x2="0" y2="6" stroke="#9aa1aa" stroke-width="2"/></pattern></defs>""").Append('\n');

        // Four vertical guides across the plot, labelled, so a value can be read off the chart.
        for (var step = 1; step <= 4; step++)
        {
            double x = labelWidth + (plot * step / 4);
            Svg.Line(svg, x, Top - 4, x, height - 40, Svg.Grid, 1, dashed: true);
            Svg.Text(svg, x, height - 26, Svg.Compact(max * step / 4), Svg.Muted, 10, "middle");
        }

        double y = Top;

        foreach (var group in groups)
        {
            if (grouped)
            {
                Svg.Text(svg, 20, y + 15, group, Svg.Ink, 12, bold: true);
                y += RowHeight;
            }

            foreach (var (_, label, value, colour, error, floor) in _bars.Where(bar => bar.Group == group))
            {
                double length = Math.Max(value / max * plot, value > 0 ? 1 : 0);
                double reach = length;

                Svg.Text(svg, labelWidth - 10, y + 15, label, Svg.Ink, 11, "end");
                Svg.Rect(svg, labelWidth, y + 4, length, RowHeight - 8, floor ? "url(#floor)" : Svg.Series[colour % Svg.Series.Length], 2);

                if (error > 0)
                {
                    double low = labelWidth + (Math.Max(0, value - error) / max * plot);
                    double high = labelWidth + ((value + error) / max * plot);
                    double middle = y + (RowHeight / 2);

                    Svg.Line(svg, low, middle, high, middle, Svg.Ink);
                    Svg.Line(svg, low, middle - 4, low, middle + 4, Svg.Ink);
                    Svg.Line(svg, high, middle - 4, high, middle + 4, Svg.Ink);
                    reach = Math.Max(reach, high - labelWidth);
                }

                Svg.Text(svg, labelWidth + reach + 6, y + 15, Svg.Compact(value), Svg.Muted, 10);
                y += RowHeight;
            }

            if (grouped)
            {
                y += GroupGap;
            }
        }

        Svg.Line(svg, labelWidth, Top - 4, labelWidth, height - 40, Svg.Muted);
        Svg.Text(svg, labelWidth, height - 12, unit, Svg.Muted, 10);

        return Svg.Close(svg);
    }
}

/// <summary>
/// A line chart for a curve: cost against a parameter that multiplies (logarithmic x axis), or a quantity sampled
/// over time (linear x axis).
/// </summary>
internal sealed class LineChart(string title, string xLabel, string yLabel, bool logarithmicX = true, bool logarithmicY = false)
{
    private const double Width = 980;
    private const double Height = 440;
    private const double Left = 84;
    private const double Right = 230;
    private const double Top = 76;
    private const double Bottom = 56;

    private readonly List<(string Name, List<(double X, double Y)> Points)> _series = [];

    internal void Add(string name, IEnumerable<(double X, double Y)> points) =>
        _series.Add((name, [.. points.OrderBy(point => point.X)]));

    internal int Count => _series.Count;

    internal string Render()
    {
        var all = _series.SelectMany(series => series.Points).ToList();

        if (all.Count == 0)
        {
            return Svg.Close(Svg.Open(Width, 120, title, "no data"));
        }

        double plotWidth = Width - Left - Right;
        double plotHeight = Height - Top - Bottom;

        double minX = logarithmicX ? Math.Max(all.Min(point => point.X), 1) : all.Min(point => point.X);
        double maxX = logarithmicX ? Math.Max(all.Max(point => point.X), minX * 10) : Math.Max(all.Max(point => point.X), minX + 1);
        double minY = logarithmicY ? Math.Max(all.Min(point => point.Y), 0.01) : 0;
        double maxY = Math.Max(all.Max(point => point.Y), double.Epsilon);

        if (logarithmicY)
        {
            minY = Math.Pow(10, Math.Floor(Math.Log10(minY)));
            maxY = Math.Pow(10, Math.Ceiling(Math.Log10(maxY)));
        }
        else
        {
            maxY *= 1.08;
        }

        double X(double value) => logarithmicX
            ? Left + ((Math.Log10(Math.Max(value, 1)) - Math.Log10(minX)) / (Math.Log10(maxX) - Math.Log10(minX)) * plotWidth)
            : Left + ((value - minX) / (maxX - minX) * plotWidth);

        double Y(double value) => logarithmicY
            ? Top + plotHeight - ((Math.Log10(Math.Max(value, minY)) - Math.Log10(minY)) / (Math.Log10(maxY) - Math.Log10(minY)) * plotHeight)
            : Top + plotHeight - (value / maxY * plotHeight);

        var axes = (logarithmicX, logarithmicY) switch
        {
            (true, true) => "logarithmic axes",
            (true, false) => "logarithmic x axis",
            (false, true) => "logarithmic y axis",
            _ => "linear axes",
        };

        var svg = Svg.Open(Width, Height, title, $"{yLabel} against {xLabel} — {axes}");

        var guides = logarithmicY
            ? Enumerable.Range(0, (int)Math.Round(Math.Log10(maxY / minY)) + 1).Select(power => minY * Math.Pow(10, power))
            : Enumerable.Range(0, 5).Select(step => maxY / 4 * step);

        foreach (var value in guides)
        {
            double y = Y(value);
            Svg.Line(svg, Left, y, Left + plotWidth, y, Svg.Grid, 1, dashed: true);
            Svg.Text(svg, Left - 8, y + 4, Svg.Compact(value), Svg.Muted, 10, "end");
        }

        var ticks = all.Select(point => point.X).Distinct().OrderBy(value => value).ToList();

        if (!logarithmicX && ticks.Count > 12)
        {
            ticks = [.. Enumerable.Range(0, 7).Select(step => minX + ((maxX - minX) * step / 6))];
        }

        foreach (var tick in ticks)
        {
            Svg.Text(svg, X(tick), Top + plotHeight + 18, Svg.Compact(tick), Svg.Muted, 10, "middle");
        }

        Svg.Text(svg, Left + (plotWidth / 2), Height - 14, xLabel, Svg.Ink, 11, "middle");
        Svg.Text(svg, 20, Top - 12, yLabel, Svg.Ink, 11);

        for (var index = 0; index < _series.Count; index++)
        {
            var (name, points) = _series[index];
            var colour = Svg.Series[index % Svg.Series.Length];
            var path = new StringBuilder();

            for (var step = 0; step < points.Count; step++)
            {
                path.Append(step == 0 ? 'M' : 'L').Append(Svg.Number(X(points[step].X))).Append(' ').Append(Svg.Number(Y(points[step].Y)));
            }

            svg.Append(CultureInfo.InvariantCulture, $"""<path d="{path}" fill="none" stroke="{colour}" stroke-width="2.2" stroke-linejoin="round" />""").Append('\n');

            if (points.Count <= 40)
            {
                foreach (var point in points)
                {
                    svg.Append(CultureInfo.InvariantCulture,
                        $"""<circle cx="{Svg.Number(X(point.X))}" cy="{Svg.Number(Y(point.Y))}" r="3.2" fill="{colour}" />""").Append('\n');
                }
            }

            Svg.Rect(svg, Width - Right + 16, Top + (index * 20), 12, 12, colour, 2);
            Svg.Text(svg, Width - Right + 34, Top + (index * 20) + 10, name, Svg.Ink, 10);
        }

        Svg.Line(svg, Left, Top, Left, Top + plotHeight, Svg.Muted);
        Svg.Line(svg, Left, Top + plotHeight, Left + plotWidth, Top + plotHeight, Svg.Muted);

        return Svg.Close(svg);
    }
}
