namespace BatteryIntelligence.Core.Models;

/// <summary>
/// One row of the session timeline (specification section 12;
/// docs/session-engine.md section 7) — a contiguous span the battery, screen,
/// lock or system state held steady, with the real samples at its boundaries.
/// </summary>
/// <param name="StartUtc">When this segment began.</param>
/// <param name="EndUtc"><see langword="null"/> for the most recent, still-open segment.</param>
/// <param name="Kind">Human-readable label: "Charging", "Screen ON", "Sleep", "Charger connected", etc.</param>
/// <param name="StartPercentage">Battery percentage at the nearest real sample at or before <paramref name="StartUtc"/>. Never interpolated.</param>
/// <param name="EndPercentage">Battery percentage at the nearest real sample at or before <paramref name="EndUtc"/>, or the latest known reading while open.</param>
/// <param name="Detail">Optional extra text (e.g. an inferred-event explanation).</param>
/// <param name="Inferred">
/// Whether this segment (or the event that starts it) was reconstructed from a
/// gap rather than observed directly — never presented as a measured fact
/// (docs/session-engine.md section 5).
/// </param>
public sealed record TimelineSegment(
    DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc,
    string Kind,
    double? StartPercentage,
    double? EndPercentage,
    string? Detail,
    bool Inferred);
