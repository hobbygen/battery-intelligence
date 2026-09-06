using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Batches <see cref="PowerReading"/>s and commits them to the <c>PowerSample</c>
/// table without the sampler ever waiting on disk — the power-metric twin of
/// <see cref="IBatterySampleWriteQueue"/> (docs/monitoring-dataflow.md section 6).
/// </summary>
/// <remarks>
/// Declared in Core, implemented in Data, so the Power module can enqueue without
/// referencing Data directly — the same sibling-decoupling pattern as
/// <see cref="ISessionStore"/> and <see cref="IBatterySampleWriteQueue"/>.
/// </remarks>
public interface IPowerSampleWriteQueue
{
    /// <summary>Rows waiting to be flushed.</summary>
    int PendingCount { get; }

    /// <summary>When the last successful flush completed, or null if none this session.</summary>
    DateTimeOffset? LastFlushUtc { get; }

    /// <summary>
    /// Queues one reading for the given device. Returns immediately; the flush
    /// happens on the queue's own timer/count triggers or an explicit
    /// <see cref="FlushAsync"/>.
    /// </summary>
    void Enqueue(BatteryDevice device, PowerReading reading);

    /// <summary>Forces an immediate flush — used on suspend, window close and shutdown.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
