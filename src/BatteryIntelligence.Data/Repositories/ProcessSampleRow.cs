using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// A <c>ProcessSample</c> row ready for insertion (docs/database.md section 4).
/// Every per-application figure is <see cref="DataQuality.Estimated"/> and sourced
/// from <see cref="MeasurementSource.Model"/> — permanently, by design
/// (specification section 15).
/// </summary>
internal sealed record ProcessSampleRow(
    long TimestampUtcMs,
    long? SessionId,
    int ProcessId,
    string ProcessName,
    string ApplicationKey,
    double? CpuPercent,
    long? MemoryBytes,
    int IsForeground,
    int? EstimatedPowerMw,
    double? EstimatedSharePercent,
    string EstimatorVersion,
    int DataQuality = (int)Core.Enums.DataQuality.Estimated,
    int MeasurementSource = (int)Core.Enums.MeasurementSource.Model);
