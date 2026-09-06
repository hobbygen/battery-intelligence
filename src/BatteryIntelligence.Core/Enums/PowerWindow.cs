namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// The time span the Power page's charts and min/max/avg accumulators cover
/// (docs/ui-navigation.md section 2, "Power"; specification section 13).
/// </summary>
/// <remarks>
/// Not persisted — a pure UI selection — but kept in Core so the windowing logic
/// (<c>PowerMonitoringService.GetSeries</c>/<c>GetStatistics</c>) stays testable
/// without the App.
/// </remarks>
public enum PowerWindow
{
    /// <summary>The last minute.</summary>
    OneMinute = 0,

    /// <summary>The last five minutes.</summary>
    FiveMinutes = 1,

    /// <summary>The last fifteen minutes.</summary>
    FifteenMinutes = 2,

    /// <summary>The last hour — the full extent of the live buffer.</summary>
    OneHour = 3,

    /// <summary>
    /// Since the current session started. Clamped to the one-hour live buffer
    /// when the session is older than that (docs/roadmap.md Phase 5 deviations).
    /// </summary>
    Session = 4,
}
