namespace BatteryIntelligence.Core.Models;

/// <summary>
/// What the Diagnostics page shows about the database (specification section 47).
/// </summary>
/// <param name="Exists">Whether the database file has been created yet.</param>
/// <param name="Path">Full path to the database file.</param>
/// <param name="SizeBytes">File size in bytes, 0 when <paramref name="Exists"/> is <see langword="false"/>.</param>
/// <param name="SampleRowCount">Row count in <c>BatterySample</c>.</param>
/// <param name="LastWriteUtc">When the most recent batch was committed, or <see langword="null"/> if none yet.</param>
/// <param name="LastCleanupUtc">When retention cleanup last ran, or <see langword="null"/> if never.</param>
/// <param name="PendingWrites">Rows currently queued but not yet flushed (across every write queue).</param>
/// <param name="PowerSampleRowCount">Row count in <c>PowerSample</c>.</param>
/// <param name="TemperatureSampleRowCount">Row count in <c>TemperatureSample</c>.</param>
/// <param name="ProcessSampleRowCount">Row count in <c>ProcessSample</c>.</param>
/// <param name="HealthSnapshotRowCount">Row count in <c>BatteryHealthSnapshot</c>.</param>
/// <param name="InsightRowCount">Active (non-dismissed) row count in <c>Insight</c>.</param>
/// <param name="AlertRowCount">Row count in <c>Alert</c>.</param>
public sealed record DatabaseDiagnostics(
    bool Exists,
    string Path,
    long SizeBytes,
    long SampleRowCount,
    DateTimeOffset? LastWriteUtc,
    DateTimeOffset? LastCleanupUtc,
    int PendingWrites,
    long PowerSampleRowCount = 0,
    long TemperatureSampleRowCount = 0,
    long ProcessSampleRowCount = 0,
    long HealthSnapshotRowCount = 0,
    long InsightRowCount = 0,
    long AlertRowCount = 0);
