using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// One ranked row on the App Usage page — an application (a group of processes),
/// the non-attributable <em>baseline</em> slice, or the collapsed <em>Other</em>
/// tail (docs/estimation-strategy.md section 5; specification sections 15 and 55).
/// </summary>
/// <remarks>
/// <see cref="EstimatedPowerMw"/> is <see langword="null"/> while on AC: there is
/// no battery draw to divide, so applications are only <em>ranked</em>
/// (docs/limitations.md "While charging, absolute per-app power is unavailable").
/// Every non-baseline row is <see cref="DataQuality.Estimated"/> — permanently,
/// by design.
/// </remarks>
/// <param name="ApplicationKey">Grouping key, e.g. "chrome". <c>"__baseline__"</c> / <c>"__other__"</c> for the synthetic rows.</param>
/// <param name="DisplayName">Human-readable name for the row.</param>
/// <param name="CpuPercent">Mean CPU use across the window, summed over the group's processes (0–100 per core-normalised point).</param>
/// <param name="MemoryBytes">Current working set summed over the group's processes.</param>
/// <param name="IsForeground">Whether any process in the group currently owns the foreground window.</param>
/// <param name="SharePercent">This row's share of the <em>attributable</em> budget, 0–100. The baseline row carries its share of the whole, shown separately.</param>
/// <param name="EstimatedPowerMw">Estimated mean draw in milliwatts, or <see langword="null"/> on AC.</param>
/// <param name="ProcessCount">How many live processes this row aggregates.</param>
/// <param name="IsBaseline">Whether this is the non-attributable baseline slice.</param>
/// <param name="IsOther">Whether this is the collapsed tail beyond the top N.</param>
public sealed record AppUsageEntry(
    string ApplicationKey,
    string DisplayName,
    double CpuPercent,
    long MemoryBytes,
    bool IsForeground,
    double SharePercent,
    int? EstimatedPowerMw,
    int ProcessCount,
    bool IsBaseline = false,
    bool IsOther = false)
{
    /// <summary>The reserved key for the non-attributable baseline row.</summary>
    public const string BaselineKey = "__baseline__";

    /// <summary>The reserved key for the collapsed "Other" row.</summary>
    public const string OtherKey = "__other__";
}

/// <summary>
/// A complete <c>AppEnergyV1</c> attribution over a window: the ranked
/// applications, the baseline slice shown separately, and the framing the UI must
/// present (docs/estimation-strategy.md section 5).
/// </summary>
/// <param name="Entries">Applications plus the "Other" row, ranked, most impactful first. Excludes the baseline.</param>
/// <param name="Baseline">The non-attributable slice (display backlight, radios, chipset idle), always shown separately so nothing is double-counted.</param>
/// <param name="TotalBudgetMw">Mean measured system draw over the window in milliwatts, or <see langword="null"/> on AC.</param>
/// <param name="AbsoluteAvailable">Whether absolute milliwatt figures are meaningful (on battery) or only the ranking is (on AC).</param>
/// <param name="Confidence">How much the baseline model has learned for this device.</param>
/// <param name="EstimatorVersion">The model tag, e.g. "AppEnergyV1", stamped on every persisted row.</param>
/// <param name="TimestampUtc">When this attribution was produced.</param>
public sealed record AppEnergyAttribution(
    IReadOnlyList<AppUsageEntry> Entries,
    AppUsageEntry Baseline,
    int? TotalBudgetMw,
    bool AbsoluteAvailable,
    AppEnergyConfidence Confidence,
    string EstimatorVersion,
    DateTimeOffset TimestampUtc)
{
    /// <summary>An empty attribution — no processes seen yet.</summary>
    public static AppEnergyAttribution Empty(string estimatorVersion, DateTimeOffset timestampUtc) => new(
        [],
        new AppUsageEntry(AppUsageEntry.BaselineKey, "System baseline", 0, 0, false, 0, null, 0, IsBaseline: true),
        null,
        AbsoluteAvailable: false,
        AppEnergyConfidence.Low,
        estimatorVersion,
        timestampUtc);

    /// <summary>Whether there is at least one application row to show.</summary>
    public bool HasEntries => Entries.Count > 0;
}
