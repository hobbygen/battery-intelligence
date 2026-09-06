namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// A <c>BatterySample</c> row ready for insertion: every field already resolved
/// to its native storage type (docs/database.md section 2), including the
/// internal surrogate <see cref="BatteryDeviceId"/> rather than the domain
/// <c>HardwareId</c>.
/// </summary>
internal sealed record BatterySampleRow(
    long TimestampUtcMs,
    long BatteryDeviceId,
    double? Percentage,
    int Status,
    int? RemainingMwh,
    int? FullChargeMwh,
    int? DesignMwh,
    int? VoltageMv,
    int? CurrentMa,
    int? PowerMw,
    int ScreenState,
    long? SessionId,
    int DataQuality,
    int MeasurementSource);
