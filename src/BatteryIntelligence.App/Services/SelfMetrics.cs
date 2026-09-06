using System.Diagnostics;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Interfaces;

namespace BatteryIntelligence.App.Services;

/// <summary>
/// Live footprint of this process for the Diagnostics page (docs/monitoring-dataflow.md
/// section 1). CPU % uses the same cumulative-time-delta technique as the process
/// sampler — no busy polling.
/// </summary>
public sealed class SelfMetrics : ISelfMetrics
{
    private readonly IBatterySampleWriteQueue _batteryQueue;
    private readonly IPowerSampleWriteQueue _powerQueue;
    private readonly ITemperatureSampleWriteQueue _temperatureQueue;
    private readonly IProcessSampleWriteQueue _processQueue;
    private readonly int _cpuCount = Math.Max(1, Environment.ProcessorCount);

    private readonly Lock _sync = new();
    private TimeSpan _lastCpu;
    private DateTimeOffset _lastCaptureUtc;

    public SelfMetrics(
        IBatterySampleWriteQueue batteryQueue,
        IPowerSampleWriteQueue powerQueue,
        ITemperatureSampleWriteQueue temperatureQueue,
        IProcessSampleWriteQueue processQueue)
    {
        ArgumentNullException.ThrowIfNull(batteryQueue);
        ArgumentNullException.ThrowIfNull(powerQueue);
        ArgumentNullException.ThrowIfNull(temperatureQueue);
        ArgumentNullException.ThrowIfNull(processQueue);

        _batteryQueue = batteryQueue;
        _powerQueue = powerQueue;
        _temperatureQueue = temperatureQueue;
        _processQueue = processQueue;
    }

    /// <inheritdoc />
    public SelfMetricsSnapshot Capture()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();

        double? cpuPercent = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan cpu = process.TotalProcessorTime;

        lock (_sync)
        {
            if (_lastCaptureUtc != default)
            {
                TimeSpan wall = now - _lastCaptureUtc;
                if (wall > TimeSpan.Zero)
                {
                    cpuPercent = (cpu - _lastCpu).TotalMilliseconds / wall.TotalMilliseconds * 100.0 / _cpuCount;
                }
            }

            _lastCpu = cpu;
            _lastCaptureUtc = now;
        }

        int pending = _batteryQueue.PendingCount + _powerQueue.PendingCount
            + _temperatureQueue.PendingCount + _processQueue.PendingCount;

        DateTimeOffset? lastFlush = new[]
        {
            _batteryQueue.LastFlushUtc, _powerQueue.LastFlushUtc,
            _temperatureQueue.LastFlushUtc, _processQueue.LastFlushUtc,
        }.Where(t => t is not null).DefaultIfEmpty(null).Max();

        return new SelfMetricsSnapshot(
            ProcessCpuPercent: cpuPercent is >= 0 ? cpuPercent : null,
            WorkingSetBytes: process.WorkingSet64,
            PrivateBytes: process.PrivateMemorySize64,
            GcHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
            ThreadCount: process.Threads.Count,
            PendingWrites: pending,
            LastFlushUtc: lastFlush);
    }
}
