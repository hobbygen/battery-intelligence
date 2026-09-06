using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>One row of the battery capability matrix, as detected at runtime.</summary>
/// <remarks>
/// Backs the mandatory Diagnostics page contract (docs/capability-matrix.md
/// section 6). Two independent facts are captured per specification section 26:
/// whether the value is obtainable at all (<see cref="Available"/>), and, when it
/// is, how (<see cref="Grade"/>, <see cref="Source"/>).
/// </remarks>
/// <param name="Id">Which capability-matrix row this is.</param>
/// <param name="FeatureName">Human-readable name for display.</param>
/// <param name="Available">Whether this device/machine can supply the value.</param>
/// <param name="Grade">
/// The grade the value ships at when available; <see langword="null"/> when
/// <paramref name="Available"/> is <see langword="false"/>.
/// </param>
/// <param name="Source">Which subsystem supplied (or attempted to supply) the value.</param>
/// <param name="Detail">A short, user-facing explanation — especially important when unavailable.</param>
public sealed record CapabilityRow(
    CapabilityId Id,
    string FeatureName,
    bool Available,
    DataQuality? Grade,
    MeasurementSource Source,
    string Detail);

/// <summary>
/// An immutable, timestamped result of probing every battery capability once.
/// </summary>
/// <param name="DetectedAtUtc">When detection ran.</param>
/// <param name="Rows">One row per <see cref="CapabilityId"/>.</param>
public sealed record CapabilitySnapshot(DateTimeOffset DetectedAtUtc, IReadOnlyList<CapabilityRow> Rows)
{
    /// <summary>Looks up the row for a capability, or <see langword="null"/> if this snapshot predates it.</summary>
    public CapabilityRow? Find(CapabilityId id) => Rows.FirstOrDefault(r => r.Id == id);
}
