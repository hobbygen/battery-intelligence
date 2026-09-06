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
/// Batches <see cref="PowerReading"/>s in memory and commits them to the
/// <c>PowerSample</c> table in one transaction per flush — the power-metric twin
/// of <see cref="BatterySampleWriteQueue"/> (docs/monitoring-dataflow.md
/// section 6).
/// </summary>
/// <remarks>
/// Same triggers and bounds as the battery queue: 200 rows, a 30-second timer, or
/// an explicit <see cref="FlushAsync"/>; a 1000-row cap drops the oldest routine
/// rows rather than growing without bound if the database stays unreachable
/// (specification section 44).
/// </remarks>
public sealed class PowerSampleWriteQueue : IPowerSampleWriteQueue, IHostedService, IDisposable
{
    private const int CountFlushThreshold = 200;
    private const int MaxPendingCap = 1000;
    private static readonly TimeSpan TimeFlushInterval = TimeSpan.FromSeconds(30);

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly BatteryDeviceRepository _deviceRepository = new();
    private readonly PowerSampleRepository _sampleRepository = new();
    private readonly ILogger<PowerSampleWriteQueue> _logger;
    private readonly Lock _gate = new();
    private readonly List<PendingSample> _pending = [];
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    private readonly record struct PendingSample(BatteryDevice Device, PowerReading Reading);

    private Timer? _timer;

    public PowerSampleWriteQueue(ISqliteConnectionFactory connectionFactory, ILogger<PowerSampleWriteQueue> logger)
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

    public void Enqueue(BatteryDevice device, PowerReading reading)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(reading);

        if (device.IsAggregate)
        {
            // The synthesised aggregate has no BatteryDevice row of its own.
            return;
        }

        bool shouldFlushNow;
        lock (_gate)
        {
            _pending.Add(new PendingSample(device, reading));

            if (_pending.Count > MaxPendingCap)
            {
                int overflow = _pending.Count - MaxPendingCap;
                _pending.RemoveRange(0, overflow);
                _logger.LogWarning(
                    "Power write queue exceeded its cap; dropped {Count} oldest routine sample(s).", overflow);
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
                _logger.LogError(ex, "Failed to flush {Count} power sample(s); will retry.", batch.Count);
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
            List<PowerSampleRow> rows = new(batch.Count);

            foreach (PendingSample pending in batch)
            {
                if (!deviceIds.TryGetValue(pending.Device.HardwareId, out long deviceId))
                {
                    deviceId = await _deviceRepository
                        .GetOrCreateAsync(connection, transaction, pending.Device, pending.Reading.TimestampUtc, cancellationToken)
                        .ConfigureAwait(false);
                    deviceIds[pending.Device.HardwareId] = deviceId;
                }

                rows.Add(ToRow(pending.Reading, deviceId));
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

    private static PowerSampleRow ToRow(PowerReading reading, long deviceId)
    {
        // One DataQuality/MeasurementSource pair per row: graded by the worst of
        // the populated electrical fields, sourced from whichever of power or
        // voltage is present (docs/database.md — same reduction as BatterySample).
        DataQuality rowQuality = DataQualityExtensions.Worst(
        [
            reading.PowerMw.HasValue ? reading.PowerMw.Quality : DataQuality.Measured,
            reading.VoltageMv.HasValue ? reading.VoltageMv.Quality : DataQuality.Measured,
            reading.CurrentMa.HasValue ? reading.CurrentMa.Quality : DataQuality.Measured,
        ]);

        MeasurementSource source = reading.PowerMw.HasValue ? reading.PowerMw.Source
            : reading.VoltageMv.HasValue ? reading.VoltageMv.Source
            : reading.CurrentMa.Source;

        return new PowerSampleRow(
            TimestampUtcMs: reading.TimestampUtc.ToUnixTimeMilliseconds(),
            BatteryDeviceId: deviceId,
            CurrentMa: reading.CurrentMa.Value is double c ? (int)Math.Round(c) : null,
            VoltageMv: reading.VoltageMv.Value,
            PowerMw: reading.PowerMw.Value,
            Direction: (int)reading.Direction,
            DataQuality: (int)rowQuality,
            MeasurementSource: (int)source);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _flushGate.Dispose();
    }
}
