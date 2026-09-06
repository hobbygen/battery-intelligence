using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// The most recent persisted <c>BatterySample</c> for one device — enough to
/// decide, at startup, whether an open session should be adopted or closed as
/// interrupted (docs/session-engine.md section 6).
/// </summary>
public sealed record LastBatterySample(DateTimeOffset TimestampUtc, BatteryState State, double? Percentage);
