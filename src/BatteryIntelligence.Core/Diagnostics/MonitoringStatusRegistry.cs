namespace BatteryIntelligence.Core.Diagnostics;

/// <inheritdoc cref="IMonitoringStatusRegistry"/>
public sealed class MonitoringStatusRegistry : IMonitoringStatusRegistry
{
    /// <summary>Consecutive failed ticks before a component is reported <see cref="MonitoringHealth.Degraded"/>.</summary>
    public const int DegradedThreshold = 3;

    private static readonly MonitoringComponent[] AllComponents = Enum.GetValues<MonitoringComponent>();

    private readonly object _sync = new();
    private readonly Dictionary<MonitoringComponent, MonitoringStatus> _statuses = [];
    private readonly TimeProvider _time;

    public MonitoringStatusRegistry()
        : this(TimeProvider.System)
    {
    }

    public MonitoringStatusRegistry(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _time = timeProvider;

        foreach (MonitoringComponent component in AllComponents)
        {
            _statuses[component] = MonitoringStatus.Starting(component);
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void ReportSuccess(MonitoringComponent component)
    {
        bool changed;
        lock (_sync)
        {
            MonitoringStatus current = _statuses[component];
            MonitoringStatus updated = current with
            {
                Health = MonitoringHealth.Healthy,
                LastError = null,
                LastSuccessUtc = _time.GetUtcNow(),
                ConsecutiveFailures = 0,
            };

            changed = RenderedStateDiffers(current, updated);
            _statuses[component] = updated;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void ReportFailure(MonitoringComponent component, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        bool changed;
        lock (_sync)
        {
            MonitoringStatus current = _statuses[component];
            int failures = current.ConsecutiveFailures + 1;

            // One or two failures is Retrying (the sampler is backing off but may
            // recover); the threshold makes it Degraded.
            MonitoringHealth health = failures >= DegradedThreshold
                ? MonitoringHealth.Degraded
                : MonitoringHealth.Retrying;

            MonitoringStatus updated = current with
            {
                Health = health,
                LastError = error,
                LastFailureUtc = _time.GetUtcNow(),
                ConsecutiveFailures = failures,
            };

            changed = RenderedStateDiffers(current, updated);
            _statuses[component] = updated;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MonitoringStatus> Snapshot()
    {
        lock (_sync)
        {
            return [.. AllComponents.Select(c => _statuses[c])];
        }
    }

    /// <summary>
    /// Whether two statuses would render differently on the Diagnostics page —
    /// the health, the error text, or crossing from "no activity" to "has
    /// activity". A repeat success with only a newer timestamp does not count.
    /// </summary>
    private static bool RenderedStateDiffers(MonitoringStatus a, MonitoringStatus b) =>
        a.Health != b.Health ||
        !string.Equals(a.LastError, b.LastError, StringComparison.Ordinal) ||
        (a.LastSuccessUtc is null) != (b.LastSuccessUtc is null);
}
