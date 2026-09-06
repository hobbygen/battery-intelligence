using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// A charging or discharging session — the public, persisted-shape summary
/// (docs/database.md, <c>BatterySession</c>; docs/session-engine.md section 1).
/// </summary>
/// <remarks>
/// A session is a contiguous period the battery moves in one direction. Screen
/// off, lock and sleep are events <em>within</em> a session, never boundaries of
/// one — conflating them is the specific failure mode
/// docs/session-engine.md section 1 warns against.
/// </remarks>
public sealed record BatterySessionInfo
{
    /// <summary>The database row id, or <see langword="null"/> for a session not yet persisted.</summary>
    public long? Id { get; init; }

    public required string BatteryId { get; init; }

    public required SessionType Type { get; init; }

    public required DateTimeOffset StartUtc { get; init; }

    /// <summary><see langword="null"/> while the session is open.</summary>
    public DateTimeOffset? EndUtc { get; init; }

    public double? StartPercentage { get; init; }

    public double? EndPercentage { get; init; }

    public int? StartCapacityMwh { get; init; }

    public int? EndCapacityMwh { get; init; }

    public long ScreenOnSeconds { get; init; }

    public long ScreenOffSeconds { get; init; }

    public long SleepSeconds { get; init; }

    public int Interruptions { get; init; }

    /// <summary>Whether the session closed on a real observed transition rather than being recovered after a crash.</summary>
    public bool ClosedCleanly { get; init; }

    /// <summary><see langword="null"/> while the session is open.</summary>
    public SessionEndReason? EndReason { get; init; }

    /// <summary>Whether this session is still in progress.</summary>
    public bool IsOpen => EndUtc is null;
}
