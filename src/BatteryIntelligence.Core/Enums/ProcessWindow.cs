namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// The time span the App Usage page's ranking averages over
/// (docs/ui-navigation.md section 2, "App Usage").
/// </summary>
/// <remarks>
/// Not persisted — a pure UI selection — but kept in Core so the windowing logic
/// (<c>ProcessMonitoringService.GetRanking</c>) stays testable without the App.
/// Only "last hour" and "this session" ship in Phase 7; wider ranges need the
/// tier-aware history query layer (Phase 11), matching Power and Temperature.
/// </remarks>
public enum ProcessWindow
{
    /// <summary>The last hour — the full extent of the live buffer.</summary>
    LastHour = 0,

    /// <summary>
    /// Since the current session started. Clamped to the one-hour live buffer
    /// when the session is older than that.
    /// </summary>
    ThisSession = 1,
}
