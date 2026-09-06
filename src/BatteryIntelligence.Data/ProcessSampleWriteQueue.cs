using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data.Repositories;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Data;

/// <summary>
/// Batches <see cref="ProcessSampleBatch"/>es in memory and commits their rows to
/// the <c>ProcessSample</c> table in one transaction per flush — the process twin
/// of <see cref="PowerSampleWriteQueue"/> (docs/monitoring-dataflow.md section 6).
/// </summary>
/// <remarks>
/// Same triggers and bounds as the other queues: 200 rows, a 30-second timer, or
/// an explicit <see cref="FlushAsync"/>; a 1000-row cap drops the oldest routine
/// rows rather than growing without bound. <c>ProcessSample</c> has no battery
/// device foreign key, so unlike the power/temperature queues this one never
/// touches <c>BatteryDevice</c>.
/// </remarks>
public sealed class ProcessSampleWriteQueue : IProcessSampleWriteQueue, IHostedService, IDisposable
{
    private const int CountFlushThreshold = 200;
    private const int MaxPendingCap = 1000;
    private static readonly TimeSpan TimeFlushInterval = TimeSpan.FromSeconds(30);

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ProcessSampleRepository _repository = new();
    private readonly ILogger<ProcessSampleWriteQueue> _logger;
    private readonly Lock _gate = new();
    private readonly List<ProcessSampleRow> _pending = [];
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    private Timer? _timer;

    public ProcessSampleWriteQueue(ISqliteConnectionFactory connectionFactory, ILogger<ProcessSampleWriteQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public DateTimeOffset? LastFlushUtc { get; private set; }

    public void Enqueue(ProcessSampleBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Rows.Count == 0)
        {
            return;
        }

        long timestampMs = batch.TimestampUtc.ToUnixTimeMilliseconds();
        bool shouldFlushNow;
        lock (_gate)
        {
            foreach (ProcessSampleRecord record in batch.Rows)
            {
                _pending.Add(new ProcessSampleRow(
                    TimestampUtcMs: timestampMs,
                    SessionId: batch.SessionId,
                    ProcessId: record.ProcessId,
                    ProcessName: Truncate(record.ProcessName, 260),
                    ApplicationKey: Truncate(record.ApplicationKey, 120),
                    CpuPercent: record.CpuPercent,
                    MemoryBytes: record.MemoryBytes,
                    IsForeground: record.IsForeground ? 1 : 0,
                    EstimatedPowerMw: record.EstimatedPowerMw,
                    EstimatedSharePercent: record.EstimatedSharePercent,
                    EstimatorVersion: batch.EstimatorVersion));
            }

            if (_pending.Count > MaxPendingCap)
            {
                int overflow = _pending.Count - MaxPendingCap;
                _pending.RemoveRange(0, overflow);
                _logger.LogWarning(
                    "Process write queue exceeded its cap; dropped {Count} oldest routine row(s).", overflow);
            }

            shouldFlushNow = _pending.Count >= CountFlushThreshold;
        }

        if (shouldFlushNow)
        {
            _ = FlushAsync();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ => _ = FlushAsync(), null, TimeFlushInterval, TimeFlushInterval);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!await _flushGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            List<ProcessSampleRow> batch;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                batch = [.. _pending];
                _pending.Clear();
            }

            try
            {
                await WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                LastFlushUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Failed to flush {Count} process sample row(s); will retry.", batch.Count);
                lock (_gate)
                {
                    _pending.InsertRange(0, batch);
                }
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task WriteBatchAsync(IReadOnlyList<ProcessSampleRow> batch, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _repository.InsertBatchAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    public void Dispose()
    {
        _timer?.Dispose();
        _flushGate.Dispose();
    }
}
