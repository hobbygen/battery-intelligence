using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Buffers validated battery readings in memory and writes them to storage in
/// batches (docs/monitoring-dataflow.md section 6; docs/architecture.md section 5).
/// </summary>
/// <remarks>
/// Declared in Core so the monitoring side (which produces <see cref="BatterySnapshot"/>
/// values) and the persistence side (Data, which implements this) never
/// reference each other directly — the App composition root is the only place
/// that wires a producer to this queue, keeping Battery and Data as independent
/// siblings (docs/architecture.md section 2).
/// </remarks>
public interface IBatterySampleWriteQueue
{
    /// <summary>
    /// Queues one battery's reading for the next batch. Returns immediately —
    /// the caller (typically the UI-thread monitoring bridge) never waits on a
    /// disk write (specification section 32).
    /// </summary>
    /// <param name="snapshot">The reading to persist.</param>
    /// <param name="screenState">
    /// Display state at the time of the reading, denormalised onto the stored
    /// row rather than resolved by a join at query time (docs/database.md,
    /// "Devices" section — screen state qualifies nearly every discharge query).
    /// </param>
    /// <param name="sessionId">
    /// The database id of the session open at the time of the reading, if any —
    /// what makes retention's "never delete a row in an open session" guard
    /// meaningful (docs/database.md section 6).
    /// </param>
    void Enqueue(BatterySnapshot snapshot, ScreenState screenState = ScreenState.Unknown, long? sessionId = null);

    /// <summary>Rows currently queued but not yet committed.</summary>
    int PendingCount { get; }

    /// <summary>When the most recent batch was committed, or <see langword="null"/> if none yet.</summary>
    DateTimeOffset? LastFlushUtc { get; }

    /// <summary>
    /// Flushes the queue immediately, bypassing the time/count triggers.
    /// Used on suspend, window close and shutdown
    /// (docs/monitoring-dataflow.md section 6, "Lifecycle").
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
