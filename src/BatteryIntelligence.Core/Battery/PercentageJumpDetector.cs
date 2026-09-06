namespace BatteryIntelligence.Core.Battery;

/// <summary>
/// The "percentage jumped implausibly" check
/// (docs/monitoring-dataflow.md section 4; docs/session-engine.md section 8).
/// </summary>
/// <remarks>
/// A single pure function shared by <see cref="Sessions.SessionStateMachine"/>
/// (which decides whether a jump matters for session semantics) and
/// <c>BatteryMonitoringService</c> (which decides whether to grade the sample
/// itself Suspect) — each applies it for its own reason, but the arithmetic and
/// the threshold are defined once.
/// </remarks>
public static class PercentageJumpDetector
{
    public const double DefaultThresholdPercent = 25.0;

    /// <summary>
    /// Whether the change from <paramref name="previous"/> to <paramref name="current"/>
    /// exceeds <paramref name="thresholdPercent"/>. <see langword="false"/> when
    /// either value is unavailable — there is nothing to compare.
    /// </summary>
    public static bool IsSuspiciousJump(double? previous, double? current, double thresholdPercent = DefaultThresholdPercent) =>
        previous is double p && current is double c && Math.Abs(c - p) > thresholdPercent;
}
