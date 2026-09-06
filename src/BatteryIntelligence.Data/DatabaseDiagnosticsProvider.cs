using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Data;

/// <inheritdoc cref="IDatabaseDiagnosticsProvider"/>
public sealed class DatabaseDiagnosticsProvider : IDatabaseDiagnosticsProvider
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IBatterySampleWriteQueue _writeQueue;
    private readonly IPowerSampleWriteQueue _powerWriteQueue;
    private readonly ITemperatureSampleWriteQueue _temperatureWriteQueue;
    private readonly IProcessSampleWriteQueue _processWriteQueue;
    private readonly ILogger<DatabaseDiagnosticsProvider> _logger;

    public DatabaseDiagnosticsProvider(
        ISqliteConnectionFactory connectionFactory,
        IBatterySampleWriteQueue writeQueue,
        IPowerSampleWriteQueue powerWriteQueue,
        ITemperatureSampleWriteQueue temperatureWriteQueue,
        IProcessSampleWriteQueue processWriteQueue,
        ILogger<DatabaseDiagnosticsProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(writeQueue);
        ArgumentNullException.ThrowIfNull(powerWriteQueue);
        ArgumentNullException.ThrowIfNull(temperatureWriteQueue);
        ArgumentNullException.ThrowIfNull(processWriteQueue);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _writeQueue = writeQueue;
        _powerWriteQueue = powerWriteQueue;
        _temperatureWriteQueue = temperatureWriteQueue;
        _processWriteQueue = processWriteQueue;
        _logger = logger;
    }

    public async Task<DatabaseDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        string path = _connectionFactory.DatabasePath;

        int pendingWrites = _writeQueue.PendingCount + _powerWriteQueue.PendingCount
            + _temperatureWriteQueue.PendingCount + _processWriteQueue.PendingCount;
        DateTimeOffset? lastWrite = Later(
            Later(
                Later(_writeQueue.LastFlushUtc, _powerWriteQueue.LastFlushUtc),
                _temperatureWriteQueue.LastFlushUtc),
            _processWriteQueue.LastFlushUtc);

        if (!File.Exists(path))
        {
            return new DatabaseDiagnostics(
                Exists: false, Path: path, SizeBytes: 0, SampleRowCount: 0,
                LastWriteUtc: null, LastCleanupUtc: null, PendingWrites: pendingWrites);
        }

        // Under WAL, recently committed data lives in the -wal sidecar until the
        // next checkpoint moves it into the main file — counting only the main
        // file would understate real disk usage right after a burst of writes.
        long sizeBytes = new FileInfo(path).Length + SidecarLength(path + "-wal") + SidecarLength(path + "-shm");
        long sampleCount = 0;
        long powerSampleCount = 0;
        long temperatureSampleCount = 0;
        long processSampleCount = 0;
        long healthSnapshotCount = 0;
        long insightCount = 0;
        DateTimeOffset? lastCleanup = null;

        try
        {
            await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (SqliteCommand countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = "SELECT COUNT(*) FROM BatterySample;";
                sampleCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
            }

            await using (SqliteCommand powerCountCommand = connection.CreateCommand())
            {
                powerCountCommand.CommandText = "SELECT COUNT(*) FROM PowerSample;";
                powerSampleCount = (long)(await powerCountCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
            }

            await using (SqliteCommand temperatureCountCommand = connection.CreateCommand())
            {
                temperatureCountCommand.CommandText = "SELECT COUNT(*) FROM TemperatureSample;";
                temperatureSampleCount = (long)(await temperatureCountCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
            }

            await using (SqliteCommand processCountCommand = connection.CreateCommand())
            {
                processCountCommand.CommandText = "SELECT COUNT(*) FROM ProcessSample;";
                processSampleCount = (long)(await processCountCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
            }

            await using (SqliteCommand healthCountCommand = connection.CreateCommand())
            {
                healthCountCommand.CommandText = "SELECT COUNT(*) FROM BatteryHealthSnapshot;";
                healthSnapshotCount = (long)(await healthCountCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
            }

            await using (SqliteCommand insightCountCommand = connection.CreateCommand())
            {
                insightCountCommand.CommandText = "SELECT COUNT(*) FROM Insight WHERE Dismissed = 0;";
                insightCount = (long)(await insightCountCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
            }

            await using (SqliteCommand cleanupCommand = connection.CreateCommand())
            {
                cleanupCommand.CommandText = "SELECT LastCleanupUtc FROM DataRetentionSettings WHERE Id = 1;";
                object? result = await cleanupCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is long ms)
                {
                    lastCleanup = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // The Diagnostics page must still render something useful even if a
            // stat query fails; the file-level facts above remain valid.
            _logger.LogWarning(ex, "Could not read database statistics.");
        }

        return new DatabaseDiagnostics(
            Exists: true,
            Path: path,
            SizeBytes: sizeBytes,
            SampleRowCount: sampleCount,
            LastWriteUtc: lastWrite,
            LastCleanupUtc: lastCleanup,
            PendingWrites: pendingWrites,
            PowerSampleRowCount: powerSampleCount,
            TemperatureSampleRowCount: temperatureSampleCount,
            ProcessSampleRowCount: processSampleCount,
            HealthSnapshotRowCount: healthSnapshotCount,
            InsightRowCount: insightCount);
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null)
        {
            return b;
        }

        return b is null ? a : (a > b ? a : b);
    }

    private static long SidecarLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
