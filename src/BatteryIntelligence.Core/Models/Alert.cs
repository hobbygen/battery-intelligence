using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// One fired alert — the shape of an <c>Alert</c> row (docs/database.md;
/// specification section 20).
/// </summary>
/// <param name="Type">Which rule fired it.</param>
/// <param name="Severity">How prominently to show it.</param>
/// <param name="Title">Fixed headline.</param>
/// <param name="Message">Fixed body, with the concrete figure substituted in.</param>
/// <param name="TriggerValue">The value that tripped the rule (e.g. 18 for 18 %), or <see langword="null"/>.</param>
/// <param name="ThresholdValue">The threshold it crossed, or <see langword="null"/>.</param>
/// <param name="TimestampUtc">When it fired.</param>
public sealed record Alert(
    AlertType Type,
    AlertSeverity Severity,
    string Title,
    string Message,
    double? TriggerValue,
    double? ThresholdValue,
    DateTimeOffset TimestampUtc)
{
    /// <summary>The database id once persisted, or <see langword="null"/>.</summary>
    public long? Id { get; init; }

    /// <summary>Whether the user has dismissed it from the active list.</summary>
    public bool Acknowledged { get; init; }
}
