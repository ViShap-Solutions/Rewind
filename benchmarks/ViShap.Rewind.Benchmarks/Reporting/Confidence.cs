namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// The margin of error of a cell: the half-width of its 99.9 % confidence interval, the standard error times
/// Student's t for the cell's degrees of freedom — what BenchmarkDotNet prints as <c>Error</c>. Every interval the
/// reports and the comparison draw is this one, so "no measurable difference" means the same thing in every table.
/// </summary>
internal static class Confidence
{
    /// <summary>t(0.9995, df) for df = 1…30, then the points the table below interpolates between.</summary>
    private static readonly double[] Small =
    [
        636.619, 31.599, 12.924, 8.610, 6.869, 5.959, 5.408, 5.041, 4.781, 4.587,
        4.437, 4.318, 4.221, 4.140, 4.073, 4.015, 3.965, 3.922, 3.883, 3.850,
        3.819, 3.792, 3.768, 3.745, 3.725, 3.707, 3.690, 3.674, 3.659, 3.646,
    ];

    private static readonly (int Df, double T)[] Large = [(30, 3.646), (40, 3.551), (60, 3.460), (120, 3.373), (int.MaxValue, 3.291)];

    /// <summary>
    /// The margin for a standard error measured over <paramref name="iterations"/> iterations. A cell that records no
    /// iteration count is given the normal quantile, the narrowest the margin can be.
    /// </summary>
    internal static double Margin(double standardError, int iterations) => standardError * T(iterations - 1);

    /// <summary>Whether two intervals, mean ± margin, overlap: no measurable difference.</summary>
    internal static bool Overlap(double meanA, double marginA, double meanB, double marginB) =>
        meanA - marginA <= meanB + marginB && meanB - marginB <= meanA + marginA;

    internal static double T(int degreesOfFreedom)
    {
        if (degreesOfFreedom < 1)
        {
            return Large[^1].T;
        }

        if (degreesOfFreedom <= Small.Length)
        {
            return Small[degreesOfFreedom - 1];
        }

        for (int index = 1; index < Large.Length; index++)
        {
            var (low, high) = (Large[index - 1], Large[index]);

            if (degreesOfFreedom <= high.Df)
            {
                // Linear in 1/df, which is how the quantile approaches the normal one.
                double at = (1.0 / low.Df - 1.0 / degreesOfFreedom) / (1.0 / low.Df - (high.Df == int.MaxValue ? 0 : 1.0 / high.Df));
                return low.T + (at * (high.T - low.T));
            }
        }

        return Large[^1].T;
    }
}
