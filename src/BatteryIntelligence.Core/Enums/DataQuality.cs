namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// How a value came to be known.
/// </summary>
/// <remarks>
/// <para>
/// Numeric values are persisted directly to the database and must never be
/// renumbered. See docs/database.md section 2.
/// </para>
/// <para>
/// There is deliberately no <c>Unavailable</c> member. An unavailable value is
/// represented by the absence of a value — <see langword="null"/> in memory and
/// <c>NULL</c> in the database — so that it is impossible to store a number and
/// simultaneously claim it is unavailable.
/// </para>
/// </remarks>
public enum DataQuality
{
    /// <summary>Provenance could not be determined. Treated as untrustworthy.</summary>
    Unknown = 0,

    /// <summary>Read directly from hardware or a Windows API.</summary>
    Measured = 1,

    /// <summary>Exact arithmetic over measured inputs.</summary>
    Calculated = 2,

    /// <summary>Model output. The true value is not obtainable on this hardware.</summary>
    Estimated = 3,

    /// <summary>
    /// Structurally valid but physically implausible. Stored for diagnosis, and
    /// excluded from aggregates, rates and insights.
    /// </summary>
    Suspect = 4,
}

/// <summary>
/// Helpers for combining <see cref="DataQuality"/> values.
/// </summary>
public static class DataQualityExtensions
{
    /// <summary>
    /// Severity rank used when combining grades. Higher is worse.
    /// </summary>
    /// <remarks>
    /// This is not the same ordering as the enum's numeric values, because
    /// <see cref="DataQuality.Unknown"/> is stored as 0 for database
    /// compatibility but is nearly the worst grade in practice.
    /// </remarks>
    private static int Severity(DataQuality quality) => quality switch
    {
        DataQuality.Measured => 1,
        DataQuality.Calculated => 2,
        DataQuality.Estimated => 3,
        DataQuality.Unknown => 4,
        DataQuality.Suspect => 5,
        _ => 5,
    };

    /// <summary>
    /// Returns the worse of two grades.
    /// </summary>
    /// <remarks>
    /// This is the enforcement point for the rule in docs/estimation-strategy.md
    /// section 1: a value derived from inputs of differing grades takes the worst
    /// grade among them. A grade can degrade, never improve.
    /// </remarks>
    public static DataQuality Worst(this DataQuality left, DataQuality right) =>
        Severity(left) >= Severity(right) ? left : right;

    /// <summary>
    /// Returns the worst grade in a sequence, or <see cref="DataQuality.Unknown"/>
    /// when the sequence is empty.
    /// </summary>
    public static DataQuality Worst(IEnumerable<DataQuality> qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);

        DataQuality? worst = null;
        foreach (DataQuality quality in qualities)
        {
            worst = worst is null ? quality : worst.Value.Worst(quality);
        }

        return worst ?? DataQuality.Unknown;
    }

    /// <summary>
    /// Whether a value of this grade may be shown without an explanatory badge.
    /// Only measured values qualify; see docs/ui-navigation.md section 4.
    /// </summary>
    public static bool IsPresentedPlainly(this DataQuality quality) =>
        quality == DataQuality.Measured;

    /// <summary>
    /// Whether values of this grade may contribute to aggregates and insights.
    /// </summary>
    public static bool IsTrustworthy(this DataQuality quality) =>
        quality is DataQuality.Measured or DataQuality.Calculated or DataQuality.Estimated;
}
