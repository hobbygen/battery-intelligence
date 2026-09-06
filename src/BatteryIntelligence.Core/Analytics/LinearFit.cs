namespace BatteryIntelligence.Core.Analytics;

/// <summary>
/// Line-fitting for the degradation trend (docs/estimation-strategy.md section 4).
/// </summary>
/// <remarks>
/// Pure. The trend uses <see cref="TheilSenSlope"/> — the median of all pairwise
/// slopes — rather than ordinary least squares, so one anomalous full-charge
/// reading cannot swing the line (docs/traceability.md R-027 "noise does not move
/// the trend").
/// </remarks>
public static class LinearFit
{
    /// <summary>
    /// Theil–Sen slope: the median of the slopes between every pair of points.
    /// </summary>
    /// <param name="xs">Independent values (e.g. days since the first sample).</param>
    /// <param name="ys">Dependent values (e.g. retention percentage).</param>
    /// <returns>The median pairwise slope, or <see langword="null"/> when there are fewer than two distinct x-values.</returns>
    public static double? TheilSenSlope(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        if (xs.Count != ys.Count)
        {
            throw new ArgumentException("xs and ys must be the same length.");
        }

        List<double> slopes = [];
        for (int i = 0; i < xs.Count; i++)
        {
            for (int j = i + 1; j < xs.Count; j++)
            {
                double dx = xs[j] - xs[i];
                if (Math.Abs(dx) < double.Epsilon)
                {
                    continue;
                }

                slopes.Add((ys[j] - ys[i]) / dx);
            }
        }

        return slopes.Count == 0 ? null : Median(slopes);
    }

    /// <summary>The intercept that pairs with <paramref name="slope"/> through the median point (a robust anchor).</summary>
    public static double InterceptThroughMedian(IReadOnlyList<double> xs, IReadOnlyList<double> ys, double slope)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);

        double medianX = Median([.. xs]);
        double medianY = Median([.. ys]);
        return medianY - (slope * medianX);
    }

    /// <summary>Median absolute residual from the fitted line — a robust spread measure for confidence.</summary>
    public static double MedianAbsoluteResidual(IReadOnlyList<double> xs, IReadOnlyList<double> ys, double slope, double intercept)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);

        List<double> residuals = new(xs.Count);
        for (int i = 0; i < xs.Count; i++)
        {
            residuals.Add(Math.Abs(ys[i] - ((slope * xs[i]) + intercept)));
        }

        return residuals.Count == 0 ? 0 : Median(residuals);
    }

    /// <summary>Population standard deviation, or 0 for fewer than two values.</summary>
    public static double StandardDeviation(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            return 0;
        }

        double mean = values.Average();
        double sumSq = 0;
        foreach (double v in values)
        {
            double d = v - mean;
            sumSq += d * d;
        }

        return Math.Sqrt(sumSq / values.Count);
    }

    /// <summary>Median of <paramref name="values"/>. Mutates a copy, not the input.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = [.. values];
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
