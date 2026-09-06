namespace BatteryIntelligence.Core.Models;

/// <summary>One process's persisted figures for a single sampling tick — the shape of a <c>ProcessSample</c> row (docs/database.md section 4).</summary>
/// <param name="ProcessId">The OS process id at the time of the sample.</param>
/// <param name="ProcessName">The process image name.</param>
/// <param name="ApplicationKey">The grouping key this process resolved to.</param>
/// <param name="CpuPercent">Core-normalised CPU percentage, or <see langword="null"/> when it could not be computed.</param>
/// <param name="MemoryBytes">Working set in bytes, or <see langword="null"/>.</param>
/// <param name="IsForeground">Whether this process owned the foreground window.</param>
/// <param name="EstimatedPowerMw">The application row's estimated draw apportioned to this process, or <see langword="null"/> on AC.</param>
/// <param name="EstimatedSharePercent">The application row's share of the attributable budget, or <see langword="null"/>.</param>
public sealed record ProcessSampleRecord(
    int ProcessId,
    string ProcessName,
    string ApplicationKey,
    double? CpuPercent,
    long? MemoryBytes,
    bool IsForeground,
    int? EstimatedPowerMw,
    double? EstimatedSharePercent);

/// <summary>
/// One sampling tick's worth of <c>ProcessSample</c> rows, handed to
/// <see cref="Interfaces.IProcessSampleWriteQueue"/> without the sampler waiting
/// on disk (docs/monitoring-dataflow.md section 6).
/// </summary>
/// <param name="TimestampUtc">When the tick was taken.</param>
/// <param name="SessionId">The open battery session's database id, or <see langword="null"/> when idle.</param>
/// <param name="EstimatorVersion">The model tag stamped on every row.</param>
/// <param name="Rows">The per-process rows for this tick.</param>
public sealed record ProcessSampleBatch(
    DateTimeOffset TimestampUtc,
    long? SessionId,
    string EstimatorVersion,
    IReadOnlyList<ProcessSampleRecord> Rows);
