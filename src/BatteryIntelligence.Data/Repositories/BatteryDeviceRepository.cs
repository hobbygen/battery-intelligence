using BatteryIntelligence.Core.Models;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// Resolves a domain <see cref="BatteryDevice"/> (keyed by
/// <see cref="BatteryDevice.HardwareId"/>) to its internal surrogate row id,
/// creating or refreshing the row as needed.
/// </summary>
/// <remarks>
/// <see cref="BatteryDevice.HardwareId"/>, not an enumeration index, is what
/// survives a reboot and distinguishes a replaced battery from the original
/// (docs/database.md, "Devices"). Identity fields are only overwritten with a
/// non-null incoming value, so a source's transient failure to report, say, the
/// serial number on one read cannot erase a previously known one.
/// </remarks>
internal sealed class BatteryDeviceRepository
{
    public async Task<long> GetOrCreateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BatteryDevice device,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(device);

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO BatteryDevice
                (HardwareId, DeviceName, Manufacturer, SerialNumber, Chemistry,
                 DesignCapacityMwh, DesignVoltageMv, ReportsInMilliamps, FirstSeenUtc, LastSeenUtc, IsPresent)
            VALUES
                ($hardwareId, $deviceName, $manufacturer, $serial, $chemistry,
                 $designCapacity, $designVoltage, $reportsInMilliamps, $now, $now, 1)
            ON CONFLICT(HardwareId) DO UPDATE SET
                DeviceName         = COALESCE(excluded.DeviceName, BatteryDevice.DeviceName),
                Manufacturer       = COALESCE(excluded.Manufacturer, BatteryDevice.Manufacturer),
                SerialNumber       = COALESCE(excluded.SerialNumber, BatteryDevice.SerialNumber),
                Chemistry          = COALESCE(excluded.Chemistry, BatteryDevice.Chemistry),
                DesignCapacityMwh  = COALESCE(excluded.DesignCapacityMwh, BatteryDevice.DesignCapacityMwh),
                DesignVoltageMv    = COALESCE(excluded.DesignVoltageMv, BatteryDevice.DesignVoltageMv),
                ReportsInMilliamps = excluded.ReportsInMilliamps,
                LastSeenUtc        = excluded.LastSeenUtc,
                IsPresent          = 1
            RETURNING Id;
            """;

        command.Parameters.AddWithValue("$hardwareId", device.HardwareId);
        command.Parameters.AddWithValue("$deviceName", (object?)device.DeviceName ?? DBNull.Value);
        command.Parameters.AddWithValue("$manufacturer", (object?)device.Manufacturer ?? DBNull.Value);
        command.Parameters.AddWithValue("$serial", (object?)device.SerialNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$chemistry", (object?)device.Chemistry ?? DBNull.Value);
        command.Parameters.AddWithValue("$designCapacity", (object?)device.DesignCapacityMWh.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$designVoltage", (object?)device.DesignVoltageMv.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$reportsInMilliamps", device.ReportsInMilliamps ? 1 : 0);
        command.Parameters.AddWithValue("$now", nowUtc.ToUnixTimeMilliseconds());

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return (long)result!;
    }
}
