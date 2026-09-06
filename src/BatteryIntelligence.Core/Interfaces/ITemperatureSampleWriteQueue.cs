using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Batches <see cref="TemperatureReading"/>s and commits them to the
/// <c>TemperatureSample</c> table without the sampler ever waiting on disk — the
/// thermal twin of <see cref="IPowerSampleWriteQueue"/>
/// (docs/monitoring-dataflow.md section 6).
/// </summary>
/// <remarks>
/// Declared in Core, implemented in Data, so the Thermal module can enqueue
/// without referencing Data directly — the same sibling-decoupling pattern as
/// <see cref="IPowerSampleWriteQueue"/>.
/// </remarks>
public interface ITemperatureSampleWriteQueue
{
    /// <summary>Rows waiting to be flushed.</summary>
    int PendingCount { get; }

    /// <summary>When the last successful flush completed, or null if none this session.</summary>
    DateTimeOffset? LastFlushUtc { get; }

    /// <summary>Queues one reading for the given device. Returns immediately.</summary>
    void Enqueue(BatteryDevice device, TemperatureReading reading);

    /// <summary>Forces an immediate flush — used on suspend, window close and shutdown.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
