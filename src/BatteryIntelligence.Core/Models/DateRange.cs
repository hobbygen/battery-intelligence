namespace BatteryIntelligence.Core.Models;

/// <summary>A half-open UTC time range <c>[FromUtc, ToUtc)</c>.</summary>
/// <param name="FromUtc">Inclusive start.</param>
/// <param name="ToUtc">Exclusive end.</param>
public readonly record struct DateRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    /// <summary>The span covered.</summary>
    public TimeSpan Duration => ToUtc - FromUtc;

    /// <summary>Whether <paramref name="timestamp"/> falls in the range.</summary>
    public bool Contains(DateTimeOffset timestamp) => timestamp >= FromUtc && timestamp < ToUtc;
}
