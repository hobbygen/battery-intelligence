using BatteryIntelligence.Core.Alerts;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Alerts;

/// <summary>
/// The alert engine — hysteresis and cooldown are what stop a value hovering at a
/// threshold from storming (specification section 20). Traceability: R-086.
/// A Phase 9 exit criterion: no duplicate or storming alerts.
/// </summary>
public sealed class AlertRuleEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static AlertSettings Settings() => new()
    {
        LowBatteryEnabled = true,
        LowBatteryPercent = 20,
        CriticalBatteryEnabled = true,
        CriticalBatteryPercent = 10,
        FullyChargedEnabled = true,
        FullyChargedPercent = 100,
        HighTemperatureEnabled = true,
        HighTemperatureCelsius = 45,
        ChargerConnectedEnabled = true,
        ChargerDisconnectedEnabled = true,
        HealthDegradationEnabled = true,
        CooldownMinutes = 15,
    };

    private static AlertEvaluationInput Discharging(double percent, DateTimeOffset now, bool ac = false) => new(
        PercentagePercent: percent, State: BatteryState.Discharging, AcOnline: ac,
        TemperatureCelsius: null, DischargeRateMw: null, ChargeRateMw: null,
        BaselineDischargeRateMw: null, BaselineChargeRateMw: null, HealthScore: null,
        DegradationSlopePercentPerMonth: null, TrendConfidence: EstimateConfidence.Calculating,
        TopAppDisplayName: null, TopAppSharePercent: null, NowUtc: now);

    [Fact]
    public void LowBattery_FiresOnceCrossingDown_AndDoesNotReFireWhileBelow()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();

        Assert.Empty(engine.Evaluate(Discharging(30, T0), s, T0));                       // above
        var fired = engine.Evaluate(Discharging(18, T0.AddMinutes(1)), s, T0.AddMinutes(1));
        Assert.Single(fired);
        Assert.Equal(AlertType.LowBattery, fired[0].Type);

        // Stays below for the next hour — must never re-fire.
        for (int m = 2; m < 60; m++)
        {
            Assert.Empty(engine.Evaluate(Discharging(15, T0.AddMinutes(m)), s, T0.AddMinutes(m)));
        }
    }

    [Fact]
    public void LowBattery_ReArmsOnlyAfterRecoveringPastTheMargin()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();

        engine.Evaluate(Discharging(30, T0), s, T0);
        Assert.Single(engine.Evaluate(Discharging(18, T0.AddMinutes(1)), s, T0.AddMinutes(1)));

        // Back to 22 — still inside the re-arm margin (20 + 5), so no re-arm, no re-fire.
        Assert.Empty(engine.Evaluate(Discharging(22, T0.AddHours(1)), s, T0.AddHours(1)));
        Assert.Empty(engine.Evaluate(Discharging(18, T0.AddHours(1).AddMinutes(1)), s, T0.AddHours(1).AddMinutes(1)));

        // Now past 25 — re-armed. Drop again → fires.
        engine.Evaluate(Discharging(27, T0.AddHours(2)), s, T0.AddHours(2));
        var refired = engine.Evaluate(Discharging(17, T0.AddHours(2).AddMinutes(1)), s, T0.AddHours(2).AddMinutes(1));
        Assert.Single(refired);
    }

    [Fact]
    public void Cooldown_SuppressesASecondAlertOfTheSameType_EvenAfterAReArm()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();
        s.CooldownMinutes = 30;

        engine.Evaluate(Discharging(30, T0), s, T0);
        Assert.Single(engine.Evaluate(Discharging(18, T0), s, T0));

        // Recover past the margin (re-armed) and drop again 10 min later — cooldown blocks it.
        engine.Evaluate(Discharging(40, T0.AddMinutes(5)), s, T0.AddMinutes(5));
        Assert.Empty(engine.Evaluate(Discharging(18, T0.AddMinutes(10)), s, T0.AddMinutes(10)));

        // 31 min after the first fire, past cooldown → allowed.
        engine.Evaluate(Discharging(40, T0.AddMinutes(30)), s, T0.AddMinutes(30));
        Assert.Single(engine.Evaluate(Discharging(18, T0.AddMinutes(31)), s, T0.AddMinutes(31)));
    }

    [Fact]
    public void CriticalWins_LowDoesNotAlsoFireInTheCriticalZone()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();

        engine.Evaluate(Discharging(30, T0), s, T0);
        var fired = engine.Evaluate(Discharging(8, T0.AddMinutes(1)), s, T0.AddMinutes(1));

        Assert.Single(fired);
        Assert.Equal(AlertType.CriticalBattery, fired[0].Type);
        Assert.Equal(AlertSeverity.Critical, fired[0].Severity);
    }

    [Fact]
    public void FullyCharged_FiresOnceAt100_AndReArmsBelowTheMargin()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();

        AlertEvaluationInput full = Discharging(100, T0, ac: true) with { State = BatteryState.Full };
        Assert.Single(engine.Evaluate(full, s, T0));
        Assert.Empty(engine.Evaluate(full with { NowUtc = T0.AddHours(1) }, s, T0.AddHours(1)));

        // Drop to 90 (below 95 margin) then back to 100 → fires again.
        engine.Evaluate(full with { PercentagePercent = 90 }, s, T0.AddHours(2));
        Assert.Single(engine.Evaluate(full with { NowUtc = T0.AddHours(3) }, s, T0.AddHours(3)));
    }

    [Fact]
    public void ChargerConnectedAndDisconnected_FireOnTheAcEdge()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();

        // First observation establishes the baseline — no edge yet.
        Assert.Empty(engine.Evaluate(Discharging(50, T0, ac: false), s, T0));

        var connected = engine.Evaluate(Discharging(50, T0.AddMinutes(1), ac: true) with { State = BatteryState.Charging }, s, T0.AddMinutes(1));
        Assert.Contains(connected, a => a.Type == AlertType.ChargerConnected);

        var disconnected = engine.Evaluate(Discharging(50, T0.AddMinutes(2), ac: false), s, T0.AddMinutes(2));
        Assert.Contains(disconnected, a => a.Type == AlertType.ChargerDisconnected);

        // Staying on battery does not re-fire.
        Assert.Empty(engine.Evaluate(Discharging(48, T0.AddMinutes(3), ac: false), s, T0.AddMinutes(3)));
    }

    [Fact]
    public void HighTemperature_UsesHysteresisAroundTheThreshold()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();

        AlertEvaluationInput hot(double c, int min) => Discharging(60, T0.AddMinutes(min)) with { TemperatureCelsius = c };

        Assert.Empty(engine.Evaluate(hot(40, 0), s, T0));
        Assert.Single(engine.Evaluate(hot(46, 1), s, T0.AddMinutes(1)));
        Assert.Empty(engine.Evaluate(hot(45, 2), s, T0.AddMinutes(2)));   // still hot, no re-fire
        Assert.Empty(engine.Evaluate(hot(44, 3), s, T0.AddMinutes(3)));   // inside re-arm margin (45 - 2)
        engine.Evaluate(hot(42, 20), s, T0.AddMinutes(20));               // re-armed
        Assert.Single(engine.Evaluate(hot(47, 21), s, T0.AddMinutes(21)));
    }

    [Fact]
    public void ADisabledAlert_NeverFires()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();
        s.LowBatteryEnabled = false;

        engine.Evaluate(Discharging(30, T0), s, T0);
        Assert.DoesNotContain(
            engine.Evaluate(Discharging(5, T0.AddMinutes(1)), s, T0.AddMinutes(1)),
            a => a.Type == AlertType.LowBattery);
    }

    [Fact]
    public void HealthDegradation_FiresOnlyOnAConfirmedDecline()
    {
        AlertRuleEngine engine = new();
        AlertSettings s = Settings();

        AlertEvaluationInput trend(double slope, EstimateConfidence conf, DateTimeOffset now) =>
            Discharging(60, now) with { DegradationSlopePercentPerMonth = slope, TrendConfidence = conf };

        // Steep decline but only Low confidence — no alert.
        Assert.Empty(engine.Evaluate(trend(-2.0, EstimateConfidence.Low, T0), s, T0));

        // Same decline, Medium confidence — fires once.
        Assert.Single(engine.Evaluate(trend(-2.0, EstimateConfidence.Medium, T0.AddDays(1)), s, T0.AddDays(1)));

        // 12 hours later, still declining — the 24h cooldown blocks it.
        Assert.Empty(engine.Evaluate(trend(-2.0, EstimateConfidence.Medium, T0.AddDays(1).AddHours(12)), s, T0.AddDays(1).AddHours(12)));
    }
}
