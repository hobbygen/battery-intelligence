namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// A <c>TemperatureSample</c> row ready for insertion: every field resolved to its
/// native storage type (docs/database.md section 4). Temperature is stored in
/// deci-Kelvin, the same integer form the WMI/IOCTL sources report.
/// </summary>
internal sealed record TemperatureSampleRow(
    long TimestampUtcMs,
    long BatteryDeviceId,
    int TemperatureDk,
    int? ChargeState,
    int DataQuality,
    int MeasurementSource);
