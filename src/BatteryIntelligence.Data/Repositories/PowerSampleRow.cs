namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// A <c>PowerSample</c> row ready for insertion: every field resolved to its
/// native storage type (docs/database.md section 2), including the internal
/// surrogate <see cref="BatteryDeviceId"/> rather than the domain <c>HardwareId</c>.
/// </summary>
internal sealed record PowerSampleRow(
    long TimestampUtcMs,
    long? BatteryDeviceId,
    int? CurrentMa,
    int? VoltageMv,
    int? PowerMw,
    int Direction,
    int DataQuality,
    int MeasurementSource);
