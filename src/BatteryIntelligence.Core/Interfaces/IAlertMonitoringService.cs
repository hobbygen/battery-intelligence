using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// The application-level alert orchestrator: <c>AlertsViewModel / ShellViewModel →
/// IAlertMonitoringService → (AlertRuleEngine + IAlertStore + INotificationPresenter)</c>,
/// mirroring <see cref="IAnalyticsService"/> (specification sections 20, 21, 73).
/// </summary>
public interface IAlertMonitoringService
{
    /// <summary>The most recent alerts, newest first — the bell flyout and the Alerts history list.</summary>
    IReadOnlyList<Alert> RecentAlerts { get; }

    /// <summary>How many alerts have not been acknowledged — the title-bar bell badge.</summary>
    int UnacknowledgedCount { get; }

    /// <summary>The most recent failure, or <see langword="null"/>.</summary>
    string? LastError { get; }

    /// <summary>Whether OS notifications are being delivered, or only the in-app centre.</summary>
    bool NotificationsAvailable { get; }

    /// <summary>Raised on the thread pool after an alert fires or an acknowledgement changes the counts.</summary>
    event EventHandler? Updated;

    /// <summary>Marks one alert acknowledged.</summary>
    Task AcknowledgeAsync(long alertId, CancellationToken cancellationToken = default);

    /// <summary>Marks every active alert acknowledged.</summary>
    Task AcknowledgeAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Forces an immediate evaluation.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists and reads <c>Alert</c> rows. Declared in Core, implemented in Data —
/// the same seam as <see cref="ISessionStore"/>.
/// </summary>
public interface IAlertStore
{
    /// <summary>Inserts a fired alert and returns its database id.</summary>
    Task<long> InsertAsync(Alert alert, CancellationToken cancellationToken = default);

    /// <summary>The most recent alerts, newest first.</summary>
    Task<IReadOnlyList<Alert>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>How many alerts have <c>Acknowledged = 0</c>.</summary>
    Task<int> GetUnacknowledgedCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks one alert acknowledged.</summary>
    Task AcknowledgeAsync(long alertId, CancellationToken cancellationToken = default);

    /// <summary>Marks every unacknowledged alert acknowledged.</summary>
    Task AcknowledgeAllAsync(CancellationToken cancellationToken = default);
}
