namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// The kind of an alert (specification section 20). One value per configurable
/// alert in <see cref="Configuration.AlertSettings"/>. Persisted
/// (<c>Alert.AlertType</c>); never renumber.
/// </summary>
public enum AlertType
{
    Unknown = 0,

    /// <summary>Discharging and at or below the configured low-battery percentage.</summary>
    LowBattery = 1,

    /// <summary>Discharging and at or below the configured critical percentage.</summary>
    CriticalBattery = 2,

    /// <summary>Reached the configured fully-charged percentage.</summary>
    FullyCharged = 3,

    /// <summary>Battery temperature at or above the configured warning threshold.</summary>
    HighTemperature = 4,

    /// <summary>Discharge rate well above this battery's own recent baseline.</summary>
    RapidDischarge = 5,

    /// <summary>Charge rate well below this battery's own recent baseline.</summary>
    SlowCharging = 6,

    /// <summary>AC power was connected.</summary>
    ChargerConnected = 7,

    /// <summary>AC power was disconnected.</summary>
    ChargerDisconnected = 8,

    /// <summary>The capacity-retention trend is declining faster than noise.</summary>
    HealthDegradation = 9,

    /// <summary>One application is taking an outsized share of the estimated draw.</summary>
    HighApplicationConsumption = 10,
}

/// <summary>How prominently an alert is shown (specification section 20). Persisted; never renumber.</summary>
public enum AlertSeverity
{
    /// <summary>Neutral — e.g. charger connected, fully charged.</summary>
    Info = 0,

    /// <summary>Something worth knowing — low battery, high temperature.</summary>
    Warning = 1,

    /// <summary>Act now — critical battery.</summary>
    Critical = 2,
}
