using System.Globalization;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Alerts;

/// <summary>
/// The pure alert engine (specification section 20). Given a reading and the
/// alert settings, it returns the alerts that should <em>fire this tick</em> —
/// applying per-alert-type <strong>hysteresis</strong> (a threshold alert fires
/// on the entering edge and re-arms only once the value recovers past a margin)
/// and a <strong>cooldown</strong> (a type cannot re-fire within
/// <see cref="AlertSettings.CooldownMinutes"/>). Together these are what keeps a
/// value oscillating around a threshold from storming.
/// </summary>
/// <remarks>
/// Stateful but hardware-free and clock-injected, so the "no storming" behaviour
/// is unit-tested with a fake clock. One instance per running application.
/// </remarks>
public sealed class AlertRuleEngine
{
    /// <summary>Health-degradation is a slow signal — it gets a 24-hour cooldown regardless of the configured value.</summary>
    private static readonly TimeSpan HealthDegradationCooldown = TimeSpan.FromHours(24);

    private const double PercentReArmMargin = 5.0;
    private const double TemperatureReArmMargin = 2.0;
    private const double HealthDegradationSlopeThreshold = 1.0;   // points/month
    private const double HighAppShareThreshold = 70.0;
    private const double HighAppShareReArm = 55.0;

    private sealed class RuleState
    {
        public bool Armed = true;
        public DateTimeOffset? LastFiredUtc;
    }

    private readonly Dictionary<AlertType, RuleState> _state = [];
    private bool? _lastAcOnline;
    private string? _lastTopApp;

    /// <summary>Evaluates every enabled rule against <paramref name="input"/>.</summary>
    public IReadOnlyList<Alert> Evaluate(AlertEvaluationInput input, AlertSettings alerts, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(alerts);

        List<Alert> fired = [];
        bool discharging = input.State == BatteryState.Discharging;

        // --- Low battery ------------------------------------------------
        if (alerts.LowBatteryEnabled && input.PercentagePercent is double lowPct)
        {
            bool trip = discharging && lowPct <= alerts.LowBatteryPercent && lowPct > alerts.CriticalBatteryPercent;
            bool reArm = !discharging || lowPct >= alerts.LowBatteryPercent + PercentReArmMargin;
            TryRule(AlertType.LowBattery, trip, reArm, nowUtc, alerts.CooldownMinutes, fired, () => new Alert(
                AlertType.LowBattery, AlertSeverity.Warning,
                Fmt($"Battery at {lowPct:F0}%"),
                Fmt($"You're at {lowPct:F0}% and running on battery. Consider plugging in soon."),
                Math.Round(lowPct, 1), alerts.LowBatteryPercent, nowUtc));
        }

        // --- Critical battery -----------------------------------------
        if (alerts.CriticalBatteryEnabled && input.PercentagePercent is double critPct)
        {
            bool trip = discharging && critPct <= alerts.CriticalBatteryPercent;
            bool reArm = !discharging || critPct >= alerts.CriticalBatteryPercent + PercentReArmMargin;
            TryRule(AlertType.CriticalBattery, trip, reArm, nowUtc, alerts.CooldownMinutes, fired, () => new Alert(
                AlertType.CriticalBattery, AlertSeverity.Critical,
                Fmt($"Battery critically low ({critPct:F0}%)"),
                "Plug in now to avoid an unexpected shutdown.",
                Math.Round(critPct, 1), alerts.CriticalBatteryPercent, nowUtc));
        }

        // --- Fully charged -------------------------------------------
        if (alerts.FullyChargedEnabled && input.PercentagePercent is double fullPct)
        {
            bool trip = fullPct >= alerts.FullyChargedPercent;
            bool reArm = fullPct < alerts.FullyChargedPercent - PercentReArmMargin;
            TryRule(AlertType.FullyCharged, trip, reArm, nowUtc, alerts.CooldownMinutes, fired, () => new Alert(
                AlertType.FullyCharged, AlertSeverity.Info,
                "Fully charged",
                Fmt($"The battery has reached {fullPct:F0}%. Unplugging now avoids leaving it to sit at full charge."),
                Math.Round(fullPct, 1), alerts.FullyChargedPercent, nowUtc));
        }

        // --- High temperature ---------------------------------------
        if (alerts.HighTemperatureEnabled && input.TemperatureCelsius is double temp)
        {
            bool trip = temp >= alerts.HighTemperatureCelsius;
            bool reArm = temp < alerts.HighTemperatureCelsius - TemperatureReArmMargin;
            TryRule(AlertType.HighTemperature, trip, reArm, nowUtc, alerts.CooldownMinutes, fired, () => new Alert(
                AlertType.HighTemperature, AlertSeverity.Warning,
                Fmt($"Battery running hot ({temp:F0} °C)"),
                Fmt($"The battery is at {temp:F0} °C, above your {alerts.HighTemperatureCelsius} °C warning threshold. Sustained heat accelerates capacity loss."),
                Math.Round(temp, 1), alerts.HighTemperatureCelsius, nowUtc));
        }

        // --- Rapid discharge ----------------------------------------
        if (alerts.RapidDischargeEnabled && discharging
            && input.DischargeRateMw is double dr && input.BaselineDischargeRateMw is double drBase && drBase > 0)
        {
            double ratio = dr / drBase;
            TryRule(AlertType.RapidDischarge, ratio >= 1.5, ratio < 1.2, nowUtc, alerts.CooldownMinutes, fired, () => new Alert(
                AlertType.RapidDischarge, AlertSeverity.Warning,
                "Draining unusually fast",
                Fmt($"Discharge is about {(ratio - 1) * 100:F0}% above your recent average. Check the App Usage page."),
                Math.Round(dr, 0), Math.Round(drBase, 0), nowUtc));
        }

        // --- Slow charging ----------------------------------------
        if (alerts.SlowChargingEnabled && input.State == BatteryState.Charging
            && input.ChargeRateMw is double cr && input.BaselineChargeRateMw is double crBase && crBase > 0)
        {
            double ratio = cr / crBase;
            TryRule(AlertType.SlowCharging, ratio <= 0.6, ratio > 0.8, nowUtc, alerts.CooldownMinutes, fired, () => new Alert(
                AlertType.SlowCharging, AlertSeverity.Info,
                "Charging slowly",
                Fmt($"Charge rate is about {(1 - ratio) * 100:F0}% below your recent average — often a lower-power charger or a busy port."),
                Math.Round(cr, 0), Math.Round(crBase, 0), nowUtc));
        }

        // --- Charger connected / disconnected (edge-triggered) --------
        if (input.AcOnline is bool ac)
        {
            if (alerts.ChargerConnectedEnabled && _lastAcOnline is false && ac
                && CooldownOk(GetState(AlertType.ChargerConnected), nowUtc, alerts.CooldownMinutes))
            {
                GetState(AlertType.ChargerConnected).LastFiredUtc = nowUtc;
                fired.Add(new Alert(AlertType.ChargerConnected, AlertSeverity.Info, "Charger connected", "AC power was connected.", null, null, nowUtc));
            }

            if (alerts.ChargerDisconnectedEnabled && _lastAcOnline is true && !ac
                && CooldownOk(GetState(AlertType.ChargerDisconnected), nowUtc, alerts.CooldownMinutes))
            {
                GetState(AlertType.ChargerDisconnected).LastFiredUtc = nowUtc;
                fired.Add(new Alert(AlertType.ChargerDisconnected, AlertSeverity.Info, "Running on battery", "AC power was disconnected.", null, null, nowUtc));
            }

            _lastAcOnline = ac;
        }

        // --- Health degradation ------------------------------------
        if (alerts.HealthDegradationEnabled && input.DegradationSlopePercentPerMonth is double slope)
        {
            bool confident = input.TrendConfidence >= EstimateConfidence.Medium;
            bool trip = confident && slope <= -HealthDegradationSlopeThreshold;
            bool reArm = !confident || slope > -(HealthDegradationSlopeThreshold - 0.3);
            RuleState st = GetState(AlertType.HealthDegradation);
            if (reArm)
            {
                st.Armed = true;
            }
            else if (trip && st.Armed
                && (st.LastFiredUtc is null || nowUtc - st.LastFiredUtc.Value >= HealthDegradationCooldown))
            {
                st.Armed = false;
                st.LastFiredUtc = nowUtc;
                fired.Add(new Alert(
                    AlertType.HealthDegradation, AlertSeverity.Warning,
                    "Capacity is trending down",
                    Fmt($"Capacity retention has fallen about {-slope:F1} percentage points per month recently. See the Battery page for the trend."),
                    Math.Round(slope, 2), -HealthDegradationSlopeThreshold, nowUtc));
            }
        }

        // --- High application consumption -------------------------
        if (alerts.HighApplicationConsumptionEnabled && discharging
            && input.TopAppSharePercent is double share && input.TopAppDisplayName is string app)
        {
            bool sameApp = string.Equals(app, _lastTopApp, StringComparison.Ordinal);
            bool trip = share >= HighAppShareThreshold;
            bool reArm = share < HighAppShareReArm || !sameApp;
            TryRule(AlertType.HighApplicationConsumption, trip, reArm, nowUtc, alerts.CooldownMinutes, fired, () => new Alert(
                AlertType.HighApplicationConsumption, AlertSeverity.Warning,
                Fmt($"{app} is using a lot of power"),
                Fmt($"{app} accounts for about {share:F0}% of your estimated battery draw."),
                Math.Round(share, 1), HighAppShareThreshold, nowUtc));
            _lastTopApp = app;
        }

        return fired;
    }

    private void TryRule(
        AlertType type, bool trip, bool reArm, DateTimeOffset nowUtc, int cooldownMinutes,
        List<Alert> fired, Func<Alert> build)
    {
        RuleState st = GetState(type);

        if (reArm)
        {
            st.Armed = true;
        }

        if (trip && st.Armed && CooldownOk(st, nowUtc, cooldownMinutes))
        {
            st.Armed = false;
            st.LastFiredUtc = nowUtc;
            fired.Add(build());
        }
    }

    private RuleState GetState(AlertType type)
    {
        if (!_state.TryGetValue(type, out RuleState? st))
        {
            st = new RuleState();
            _state[type] = st;
        }

        return st;
    }

    private static bool CooldownOk(RuleState st, DateTimeOffset nowUtc, int cooldownMinutes) =>
        st.LastFiredUtc is null || nowUtc - st.LastFiredUtc.Value >= TimeSpan.FromMinutes(Math.Max(0, cooldownMinutes));

    private static string Fmt(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
