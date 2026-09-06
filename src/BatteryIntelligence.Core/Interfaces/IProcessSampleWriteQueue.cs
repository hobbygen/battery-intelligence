using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Batches <see cref="ProcessSampleBatch"/>es and commits them to the
/// <c>ProcessSample</c> table without the sampler ever waiting on disk — the
/// process twin of <see cref="IPowerSampleWriteQueue"/>
/// (docs/monitoring-dataflow.md section 6).
/// </summary>
/// <remarks>
/// Declared in Core, implemented in Data, so the ProcessMonitoring module can
/// enqueue without referencing Data directly.
/// </remarks>
public interface IProcessSampleWriteQueue
{
    /// <summary>Rows waiting to be flushed.</summary>
    int PendingCount { get; }

    /// <summary>When the last successful flush completed, or <see langword="null"/> if none this session.</summary>
    DateTimeOffset? LastFlushUtc { get; }

    /// <summary>Queues one tick's rows. Returns immediately.</summary>
    void Enqueue(ProcessSampleBatch batch);

    /// <summary>Forces an immediate flush — used on suspend, window close and shutdown.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
