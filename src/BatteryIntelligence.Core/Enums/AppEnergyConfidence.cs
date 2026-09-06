namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// How much to trust an <c>AppEnergyV1</c> attribution (docs/estimation-strategy.md
/// section 5). Distinct from <see cref="DataQuality"/>: a per-app figure is
/// <em>always</em> <see cref="DataQuality.Estimated"/>; this says how good that
/// estimate is likely to be given how much the baseline model has learned.
/// </summary>
/// <remarks>
/// Numeric values are persisted (inside <c>SupportingJson</c> and surfaced on the
/// page) and must never be renumbered.
/// </remarks>
public enum AppEnergyConfidence
{
    /// <summary>
    /// The baseline is still a conservative default — not enough idle history to
    /// separate display and platform draw per this device.
    /// </summary>
    Low = 0,

    /// <summary>The baseline has been observed but the sample is thin.</summary>
    Medium = 1,

    /// <summary>The baseline is derived from a solid idle-draw history for this device.</summary>
    High = 2,
}
