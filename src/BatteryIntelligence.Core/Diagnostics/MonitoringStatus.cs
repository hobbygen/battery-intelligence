namespace BatteryIntelligence.Core.Diagnostics;

/// <summary>
/// A monitored subsystem, for the Diagnostics page's per-subsystem health row
/// (specification section 26; docs/monitoring-dataflow.md section 8). Persisted
/// nowhere — a live view only.
/// </summary>
public enum MonitoringComponent
{
    Battery = 0,
    Power = 1,
    Temperature = 2,
    ApplicationUsage = 3,
    Sessions = 4,
    Analytics = 5,
    Alerts = 6,
    Database = 7,
}

/// <summary>
/// The health of one subsystem. A simplification of the
/// <c>Healthy → Retrying → Degraded</c> model in docs/monitoring-dataflow.md
/// section 8 — the retry/backoff layer is not built yet, so only the two states
/// the Diagnostics page needs are surfaced, plus <see cref="Starting"/> for the
/// window before a subsystem's first tick.
/// </summary>
public enum MonitoringHealth
{
    /// <summary>No tick has succeeded or failed yet.</summary>
    Starting = 0,

    /// <summary>The last tick succeeded.</summary>
    Healthy = 1,

    /// <summary>At least <c>MonitoringStatusRegistry.DegradedThreshold</c> consecutive ticks have failed.</summary>
    Degraded = 2,
}

/// <summary>One subsystem's current monitoring state (specification section 26).</summary>
/// <param name="Component">Which subsystem.</param>
/// <param name="Health">Its current health.</param>
/// <param name="LastError">The most recent failure message, or <see langword="null"/> if the last tick succeeded.</param>
/// <param name="LastSuccessUtc">When a tick last succeeded, or <see langword="null"/>.</param>
/// <param name="LastFailureUtc">When a tick last failed, or <see langword="null"/>.</param>
/// <param name="ConsecutiveFailures">Failed ticks since the last success.</param>
public sealed record MonitoringStatus(
    MonitoringComponent Component,
    MonitoringHealth Health,
    string? LastError,
    DateTimeOffset? LastSuccessUtc,
    DateTimeOffset? LastFailureUtc,
    int ConsecutiveFailures)
{
    /// <summary>The starting state for a component that has not reported yet.</summary>
    public static MonitoringStatus Starting(MonitoringComponent component) =>
        new(component, MonitoringHealth.Starting, null, null, null, 0);
}

/// <summary>Display helpers for <see cref="MonitoringComponent"/>.</summary>
public static class MonitoringComponentExtensions
{
    /// <summary>A human-readable name for the Diagnostics row.</summary>
    public static string Describe(this MonitoringComponent component) => component switch
    {
        MonitoringComponent.Battery => "Battery monitoring",
        MonitoringComponent.Power => "Power monitoring",
        MonitoringComponent.Temperature => "Temperature monitoring",
        MonitoringComponent.ApplicationUsage => "Application usage",
        MonitoringComponent.Sessions => "Session tracking",
        MonitoringComponent.Analytics => "Analytics",
        MonitoringComponent.Alerts => "Alert evaluation",
        MonitoringComponent.Database => "Database rollup & retention",
        _ => component.ToString(),
    };
}
