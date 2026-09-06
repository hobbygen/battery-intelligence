using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data.Repositories;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Data;

/// <summary>
/// Batches battery readings in memory and commits them to SQLite in one
/// transaction per flush (docs/monitoring-dataflow.md section 6).
/// </summary>
/// <remarks>
/// Flush triggers, whichever comes first: 200 queued rows, a 30-second timer, or
/// an explicit <see cref="FlushAsync"/> call (suspend, window close, shutdown).
/// If the database stays unreachable, pending rows are capped and the oldest are
/// dropped rather than growing without bound — a monitoring process must not
/// exhaust memory because a disk is unavailable (specification section 44,
/// docs/architecture.md driver 3).
/// </remarks>
public sealed class BatterySampleWriteQueue : IBatterySampleWriteQueue, IHostedService, IDisposable
{
    private const int CountFlushThreshold = 200;
    private const int MaxPendingCap = 1000;
    private static readonly TimeSpan TimeFlushInterval = TimeSpan.FromSeconds(30);

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly BatteryDeviceRepository _deviceRepository = new();
    private readonly BatterySampleRepository _sampleRepository = new();
    private readonly ILogger<BatterySampleWriteQueue> _logger;
    private readonly Lock _gate = new();
    private readonly List<PendingSample> _pending = [];
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    private readonly record struct PendingSample(BatterySnapshot Snapshot, ScreenState ScreenState, long? SessionId);

    private Timer? _timer;

    public BatterySampleWriteQueue(ISqliteConnectionFactory connectionFactory, ILogger<BatterySampleWriteQueue> logger)
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

    public void Enqueue(BatterySnapshot snapshot, ScreenState screenState = ScreenState.Unknown, long? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Device.IsAggregate)
        {
            // The synthesised multi-battery aggregate is a UI convenience, not a
            // physical device — it has no row of its own in BatteryDevice and
            // must never be persisted as though it were one.
            return;
        }

        bool shouldFlushNow;
        lock (_gate)
        {
            _pending.Add(new PendingSample(snapshot, screenState, sessionId));

            if (_pending.Count > MaxPendingCap)
            {
                int overflow = _pending.Count - MaxPendingCap;
                _pending.RemoveRange(0, overflow);
                _logger.LogWarning(
                    "Write queue exceeded its cap; dropped {Count} oldest routine sample(s).", overflow);
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

        // Lifecycle flush: shutdown must not silently discard whatever is still
        // queued (docs/monitoring-dataflow.md section 6, "Lifecycle").
        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!await _flushGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // A flush is already running (timer and count-trigger raced, or a
            // caller invoked FlushAsync while one was in progress) — not an
            // error; the in-flight flush will pick up everything queued so far.
            return;
        }

        try
        {
            List<PendingSample> batch;
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
                // The database is unreachable (locked, disk full, missing
                // directory). Requeue so the next flush retries, rather than
                // losing the batch outright — Enqueue's cap still bounds memory
                // if this persists.
                _logger.LogError(ex, "Failed to flush {Count} battery sample(s); will retry.", batch.Count);
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

    private async Task WriteBatchAsync(IReadOnlyList<PendingSample> batch, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Dictionary<string, long> deviceIds = [];
            List<BatterySampleRow> rows = new(batch.Count);

            foreach (PendingSample pending in batch)
            {
                BatterySnapshot snapshot = pending.Snapshot;
                if (!deviceIds.TryGetValue(snapshot.Device.HardwareId, out long deviceId))
                {
                    deviceId = await _deviceRepository
                        .GetOrCreateAsync(connection, transaction, snapshot.Device, snapshot.Info.TimestampUtc, cancellationToken)
                        .ConfigureAwait(false);
                    deviceIds[snapshot.Device.HardwareId] = deviceId;
                }

                rows.Add(ToRow(snapshot, deviceId, pending.ScreenState, pending.SessionId));
            }

            await _sampleRepository.InsertBatchAsync(connection, transaction, rows, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static BatterySampleRow ToRow(BatterySnapshot snapshot, long deviceId, ScreenState screenState, long? sessionId)
    {
        BatteryInfo info = snapshot.Info;

        // The schema carries one DataQuality/MeasurementSource pair per row
        // (docs/database.md), while a reading carries one per field. The row is
        // graded by the worst of its populated fields, and sourced from the
        // headline percentage reading — a documented reduction, not a loss of
        // the finer-grained grading, which every consumer above this layer still
        // sees on the live BatterySnapshot.
        DataQuality rowQuality = DataQualityExtensions.Worst([
            info.Percentage.Quality, info.RemainingCapacityMWh.Quality, info.FullChargeCapacityMWh.Quality,
            info.VoltageMv.Quality, info.PowerMw.Quality,
        ]);

        return new BatterySampleRow(
            TimestampUtcMs: info.TimestampUtc.ToUnixTimeMilliseconds(),
            BatteryDeviceId: deviceId,
            Percentage: info.Percentage.Value,
            Status: (int)(info.State.Value ?? BatteryState.Unknown),
            RemainingMwh: info.RemainingCapacityMWh.Value,
            FullChargeMwh: info.FullChargeCapacityMWh.Value,
            DesignMwh: info.DesignCapacityMWh.Value,
            VoltageMv: info.VoltageMv.Value,
            CurrentMa: info.CurrentMa.Value is double current ? (int)Math.Round(current) : null,
            PowerMw: info.PowerMw.Value,
            ScreenState: (int)screenState,
            SessionId: sessionId,
            DataQuality: (int)rowQuality,
            MeasurementSource: (int)info.Percentage.Source);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _flushGate.Dispose();
    }
}
