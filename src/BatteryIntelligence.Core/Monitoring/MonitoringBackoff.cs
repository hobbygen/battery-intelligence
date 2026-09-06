namespace BatteryIntelligence.Core.Monitoring;

/// <summary>
/// Exponential retry back-off for a failing sampler (docs/monitoring-dataflow.md
/// section 8): after a failure the sampler retries less often, up to a cap, so a
/// sensor that has been absent for weeks is not probed every few seconds. Pure.
/// </summary>
public static class MonitoringBackoff
{
    /// <summary>The retry interval never grows past this, no matter how long a subsystem has been down.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The interval to wait before the next attempt. <paramref name="consecutiveFailures"/>
    /// of 0 returns <paramref name="baseInterval"/> unchanged (healthy); each
    /// further failure doubles it, capped at <see cref="MaxBackoff"/>.
    /// </summary>
    public static TimeSpan NextInterval(TimeSpan baseInterval, int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return baseInterval;
        }

        // 2^(n-1): 1 failure → ×1, 2 → ×2, 3 → ×4, …
        double factor = Math.Pow(2, consecutiveFailures - 1);
        double cappedTicks = Math.Min(baseInterval.Ticks * factor, MaxBackoff.Ticks);
        return TimeSpan.FromTicks((long)cappedTicks);
    }
}
