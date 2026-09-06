using BatteryIntelligence.Core.Diagnostics;
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
/// Batches <see cref="TemperatureReading"/>s in memory and commits them to the
/// <c>TemperatureSample</c> table in one transaction per flush — the thermal twin
/// of <see cref="PowerSampleWriteQueue"/> (docs/monitoring-dataflow.md section 6).
/// </summary>
/// <remarks>
/// Same triggers and bounds as the other queues: 200 rows, a 30-second timer, or
/// an explicit <see cref="FlushAsync"/>; a 1000-row cap drops the oldest routine
/// rows rather than growing without bound.
/// </remarks>
public sealed class TemperatureSampleWriteQueue : ITemperatureSampleWriteQueue, IHostedService, IDisposable
{
    private const int CountFlushThreshold = 200;
    private const int MaxPendingCap = 1000;
    private static readonly TimeSpan TimeFlushInterval = TimeSpan.FromSeconds(30);

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly BatteryDeviceRepository _deviceRepository = new();
    private readonly TemperatureSampleRepository _sampleRepository = new();
    private readonly ILogger<TemperatureSampleWriteQueue> _logger;
    private readonly IMonitoringStatusRegistry _status;
    private readonly Lock _gate = new();
    private readonly List<PendingSample> _pending = [];
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    private readonly record struct PendingSample(BatteryDevice Device, TemperatureReading Reading);

    private Timer? _timer;

    public TemperatureSampleWriteQueue(
        ISqliteConnectionFactory connectionFactory,
        ILogger<TemperatureSampleWriteQueue> logger,
        IMonitoringStatusRegistry status)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _connectionFactory = connectionFactory;
        _logger = logger;
        _status = status;
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

    public void Enqueue(BatteryDevice device, TemperatureReading reading)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(reading);

        if (device.IsAggregate || !reading.TemperatureCelsius.HasValue)
        {
            // Nothing to persist for the synthesised aggregate, or for an
            // unavailable reading — TemperatureSample simply stays empty on
            // hardware with no sensor (docs/database.md).
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
                    "Temperature write queue exceeded its cap; dropped {Count} oldest routine sample(s).", overflow);
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
                _logger.LogError(ex, "Failed to flush {Count} temperature sample(s); will retry.", batch.Count);
                _status.ReportFailure(MonitoringComponent.Database, ex.Message);
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
            List<TemperatureSampleRow> rows = new(batch.Count);

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

    private static TemperatureSampleRow ToRow(TemperatureReading reading, long deviceId)
    {
        double celsius = reading.TemperatureCelsius.Value!.Value;
        int deciKelvin = (int)Math.Round((celsius + 273.15) * 10.0);

        int? chargeState = reading.ChargeContext == PowerDirection.Unknown ? null : (int)reading.ChargeContext;

        return new TemperatureSampleRow(
            TimestampUtcMs: reading.TimestampUtc.ToUnixTimeMilliseconds(),
            BatteryDeviceId: deviceId,
            TemperatureDk: deciKelvin,
            ChargeState: chargeState,
            DataQuality: (int)reading.TemperatureCelsius.Quality,
            MeasurementSource: (int)reading.TemperatureCelsius.Source);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _flushGate.Dispose();
    }
}
