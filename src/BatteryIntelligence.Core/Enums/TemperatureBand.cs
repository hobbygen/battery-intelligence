namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// A coarse battery-temperature band, for the per-band time breakdown on the
/// Temperature page (docs/ui-navigation.md section 2). Boundaries are fixed
/// (30 / 40 / 45 °C) and independent of the configurable alert threshold — the
/// breakdown answers "how warm does this battery run", not "is an alert due".
/// </summary>
public enum TemperatureBand
{
    /// <summary>Below 30 °C.</summary>
    Cool = 0,

    /// <summary>30–40 °C.</summary>
    Normal = 1,

    /// <summary>40–45 °C.</summary>
    Warm = 2,

    /// <summary>Above 45 °C — the range that drives most heat-related capacity loss.</summary>
    Hot = 3,
}

/// <summary>
/// The severity of the current temperature relative to the configured alert
/// thresholds — what the status chip beside the live reading shows. Distinct from
/// <see cref="TemperatureBand"/>: this one moves when the user changes the
/// threshold.
/// </summary>
public enum TemperatureSeverity
{
    /// <summary>Below the warning threshold.</summary>
    Normal = 0,

    /// <summary>At or above the warning threshold, below critical.</summary>
    Warning = 1,

    /// <summary>At or above the critical threshold.</summary>
    Critical = 2,
}

/// <summary>Selectable window on the Temperature page (docs/ui-navigation.md section 6).</summary>
public enum TemperatureWindow
{
    /// <summary>The last hour — the width of the live buffer.</summary>
    OneHour = 0,

    /// <summary>The current session, clamped to the live buffer.</summary>
    Session = 1,
}
