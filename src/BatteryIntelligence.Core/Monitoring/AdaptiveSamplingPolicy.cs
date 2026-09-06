using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Monitoring;

/// <summary>
/// The live conditions that decide how often the samplers run
/// (docs/monitoring-dataflow.md section 3). Assembled by the orchestrators from
/// signals they already receive.
/// </summary>
/// <param name="Screen">Current display power state.</param>
/// <param name="PowerState">Aggregate battery state (charging / discharging / full / …).</param>
/// <param name="BatteryPercent">Aggregate charge percentage, or <see langword="null"/> if unknown.</param>
/// <param name="WindowVisible">Whether the main window is shown (not tray-only).</param>
/// <param name="ChargingSessionActive">Whether a charging session is in progress — charge curves matter.</param>
/// <param name="MonitoringPaused">Whether the user has paused monitoring.</param>
/// <param name="AdaptiveEnabled">The <c>MonitoringSettings.AdaptiveSampling</c> opt-out.</param>
public sealed record SamplingConditions(
    ScreenState Screen,
    BatteryState PowerState,
    double? BatteryPercent,
    bool WindowVisible,
    bool ChargingSessionActive,
    bool MonitoringPaused,
    bool AdaptiveEnabled);

/// <summary>The resolved sampling behaviour for one moment (docs/monitoring-dataflow.md section 3).</summary>
/// <param name="BatteryInterval">Interval for the battery/power pipeline.</param>
/// <param name="ProcessInterval">Interval for the process sampler.</param>
/// <param name="ProcessPaused">Whether the process sampler should skip entirely.</param>
/// <param name="AllStopped">Whether every routine sampler should stop (monitoring paused).</param>
/// <param name="ChartFeedSuspended">Whether the UI chart feed should be skipped (window hidden).</param>
public sealed record SamplingPlan(
    TimeSpan BatteryInterval,
    TimeSpan ProcessInterval,
    bool ProcessPaused,
    bool AllStopped,
    bool ChartFeedSuspended);

/// <summary>
/// Resolves <see cref="SamplingConditions"/> to a <see cref="SamplingPlan"/> — the
/// adaptive-sampling table in docs/monitoring-dataflow.md section 3, verbatim and
/// pure. Adaptive sampling never changes what is <em>recorded</em> about a
/// transition; it only throttles routine telemetry.
/// </summary>
public static class AdaptiveSamplingPolicy
{
    /// <summary>The clamp applied to every resolved interval — the <c>MonitoringSettings</c> range.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    /// <summary>The clamp applied to every resolved interval — the <c>MonitoringSettings</c> range.</summary>
    public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(300);

    /// <summary>Charge threshold below which sampling gets finer, not coarser (the interesting region).</summary>
    public const double LowBatteryPercent = 20.0;

    /// <summary>Charge threshold at or above which the battery counts as full for the AC back-off.</summary>
    public const double FullBatteryPercent = 99.0;

    /// <summary>
    /// Resolves the plan. <paramref name="baseBattery"/> and
    /// <paramref name="baseProcess"/> are the user-configured intervals.
    /// </summary>
    public static SamplingPlan Resolve(TimeSpan baseBattery, TimeSpan baseProcess, SamplingConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);

        // Monitoring paused: everything stops, nothing else matters.
        if (conditions.MonitoringPaused)
        {
            return new SamplingPlan(baseBattery, baseProcess, ProcessPaused: true, AllStopped: true, ChartFeedSuspended: true);
        }

        bool chartSuspended = !conditions.WindowVisible;

        // The honest opt-out: fixed-rate sampling, only the chart-feed skip stands.
        if (!conditions.AdaptiveEnabled)
        {
            return new SamplingPlan(
                Clamp(baseBattery), Clamp(baseProcess), ProcessPaused: false, AllStopped: false, chartSuspended);
        }

        bool discharging = conditions.PowerState == BatteryState.Discharging;
        bool screenOff = conditions.Screen == ScreenState.Off;
        bool onAc = conditions.PowerState is BatteryState.Charging or BatteryState.Idle or BatteryState.Full;
        bool full = conditions.PowerState == BatteryState.Full
            || conditions.BatteryPercent is >= FullBatteryPercent;

        // Battery < 20 % while discharging: finer, and it wins over every back-off.
        if (discharging && conditions.BatteryPercent is < LowBatteryPercent)
        {
            return new SamplingPlan(
                Clamp(baseBattery * 0.5), Clamp(baseProcess * 0.5),
                ProcessPaused: false, AllStopped: false, chartSuspended);
        }

        // A charging session is in progress: keep the base rate — the charge curve
        // is exactly what we want to capture — overriding a screen-off back-off.
        if (conditions.ChargingSessionActive)
        {
            return new SamplingPlan(
                Clamp(baseBattery), Clamp(baseProcess),
                ProcessPaused: false, AllStopped: false, chartSuspended);
        }

        if (screenOff && onAc && full)
        {
            return new SamplingPlan(
                Clamp(baseBattery * 6), Clamp(baseProcess),
                ProcessPaused: true, AllStopped: false, chartSuspended);
        }

        if (screenOff && discharging)
        {
            return new SamplingPlan(
                Clamp(baseBattery * 3), Clamp(baseProcess * 3),
                ProcessPaused: false, AllStopped: false, chartSuspended);
        }

        return new SamplingPlan(
            Clamp(baseBattery), Clamp(baseProcess),
            ProcessPaused: false, AllStopped: false, chartSuspended);
    }

    private static TimeSpan Clamp(TimeSpan interval) =>
        interval < MinInterval ? MinInterval : interval > MaxInterval ? MaxInterval : interval;
}
