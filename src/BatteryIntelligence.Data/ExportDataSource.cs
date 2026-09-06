using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data;

/// <inheritdoc cref="IExportDataSource"/>
public sealed class ExportDataSource : IExportDataSource
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public ExportDataSource(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ExportTable>> CollectAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        long fromMs = request.Range.FromUtc.ToUnixTimeMilliseconds();
        long toMs = request.Range.ToUtc.ToUnixTimeMilliseconds();

        var tables = new List<ExportTable>();
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (TableSpec spec in Specs)
        {
            if ((request.Scope & spec.Flag) == 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            tables.Add(await ReadTableAsync(connection, spec, fromMs, toMs, cancellationToken).ConfigureAwait(false));
        }

        return tables;
    }

    private static async Task<ExportTable> ReadTableAsync(
        SqliteConnection connection, TableSpec spec, long fromMs, long toMs, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {string.Join(", ", spec.Select)} FROM {spec.Table} " +
            $"WHERE {spec.RangeColumn} >= $from AND {spec.RangeColumn} < $to ORDER BY {spec.RangeColumn};";
        command.Parameters.AddWithValue("$from", fromMs);
        command.Parameters.AddWithValue("$to", toMs);

        var rows = new List<IReadOnlyList<string?>>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new string?[spec.Columns.Count];
            for (int c = 0; c < spec.Columns.Count; c++)
            {
                row[c] = spec.Format(c, reader);
            }

            rows.Add(row);
        }

        return new ExportTable(spec.Table, spec.Columns, rows);
    }

    private static string? Scalar(SqliteDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
        {
            return null;
        }

        return reader.GetValue(i) switch
        {
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            int n => n.ToString(CultureInfo.InvariantCulture),
            _ => reader.GetString(i),
        };
    }

    private static string? IsoTime(SqliteDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(i)).ToString("O", CultureInfo.InvariantCulture);

    private static string? Celsius(SqliteDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : (reader.GetInt64(i) / 10.0 - 273.15).ToString("F2", CultureInfo.InvariantCulture);

    private static string? Bool(SqliteDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : reader.GetInt64(i) != 0 ? "true" : "false";

    private static string? EnumName<TEnum>(SqliteDataReader reader, int i) where TEnum : struct, Enum
    {
        if (reader.IsDBNull(i))
        {
            return null;
        }

        int value = (int)reader.GetInt64(i);
        return Enum.IsDefined(typeof(TEnum), value)
            ? ((TEnum)(object)value).ToString()
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record TableSpec(
        ExportScope Flag,
        string Table,
        string RangeColumn,
        IReadOnlyList<string> Select,
        IReadOnlyList<string> Columns,
        Func<int, SqliteDataReader, string?> Format);

    private static readonly IReadOnlyList<TableSpec> Specs =
    [
        new(ExportScope.BatterySamples, "BatterySample", "TimestampUtc",
            ["TimestampUtc", "Percentage", "Status", "RemainingMwh", "FullChargeMwh", "VoltageMv", "CurrentMa", "PowerMw", "ScreenState", "DataQuality", "MeasurementSource"],
            ["TimestampUtc", "Percentage", "Status", "RemainingMwh", "FullChargeMwh", "VoltageMv", "CurrentMa", "PowerMw", "ScreenState", "DataQuality", "MeasurementSource"],
            (c, r) => c switch
            {
                0 => IsoTime(r, 0),
                2 => EnumName<BatteryState>(r, 2),
                8 => EnumName<ScreenState>(r, 8),
                9 => EnumName<DataQuality>(r, 9),
                10 => EnumName<MeasurementSource>(r, 10),
                _ => Scalar(r, c),
            }),

        new(ExportScope.PowerSamples, "PowerSample", "TimestampUtc",
            ["TimestampUtc", "CurrentMa", "VoltageMv", "PowerMw", "Direction", "DataQuality", "MeasurementSource"],
            ["TimestampUtc", "CurrentMa", "VoltageMv", "PowerMw", "Direction", "DataQuality", "MeasurementSource"],
            (c, r) => c switch
            {
                0 => IsoTime(r, 0),
                4 => EnumName<PowerDirection>(r, 4),
                5 => EnumName<DataQuality>(r, 5),
                6 => EnumName<MeasurementSource>(r, 6),
                _ => Scalar(r, c),
            }),

        new(ExportScope.TemperatureSamples, "TemperatureSample", "TimestampUtc",
            ["TimestampUtc", "TemperatureDk", "ChargeState", "DataQuality", "MeasurementSource"],
            ["TimestampUtc", "TemperatureCelsius", "ChargeState", "DataQuality", "MeasurementSource"],
            (c, r) => c switch
            {
                0 => IsoTime(r, 0),
                1 => Celsius(r, 1),
                2 => EnumName<PowerDirection>(r, 2),
                3 => EnumName<DataQuality>(r, 3),
                4 => EnumName<MeasurementSource>(r, 4),
                _ => Scalar(r, c),
            }),

        new(ExportScope.Sessions, "BatterySession", "StartUtc",
            ["Id", "SessionType", "StartUtc", "EndUtc", "StartPercentage", "EndPercentage", "ScreenOnSeconds", "ScreenOffSeconds", "SleepSeconds", "AvgRateMw", "PeakTemperatureDk", "Interruptions", "QualityScore", "ClosedCleanly", "EndReason"],
            ["Id", "SessionType", "StartUtc", "EndUtc", "StartPercentage", "EndPercentage", "ScreenOnSeconds", "ScreenOffSeconds", "SleepSeconds", "AvgRateMw", "PeakTemperatureCelsius", "Interruptions", "QualityScore", "ClosedCleanly", "EndReason"],
            (c, r) => c switch
            {
                1 => EnumName<SessionType>(r, 1),
                2 => IsoTime(r, 2),
                3 => IsoTime(r, 3),
                10 => Celsius(r, 10),
                13 => Bool(r, 13),
                14 => EnumName<SessionEndReason>(r, 14),
                _ => Scalar(r, c),
            }),

        new(ExportScope.Alerts, "Alert", "TimestampUtc",
            ["TimestampUtc", "AlertType", "Severity", "Title", "Message", "TriggerValue", "ThresholdValue", "Acknowledged"],
            ["TimestampUtc", "AlertType", "Severity", "Title", "Message", "TriggerValue", "ThresholdValue", "Acknowledged"],
            (c, r) => c switch
            {
                0 => IsoTime(r, 0),
                1 => EnumName<AlertType>(r, 1),
                2 => EnumName<AlertSeverity>(r, 2),
                7 => Bool(r, 7),
                _ => Scalar(r, c),
            }),

        new(ExportScope.HealthSnapshots, "BatteryHealthSnapshot", "TimestampUtc",
            ["TimestampUtc", "FullChargeMwh", "DesignMwh", "RetentionPercent", "CycleCount", "HealthScore", "HealthCategory", "AlgorithmVersion"],
            ["TimestampUtc", "FullChargeMwh", "DesignMwh", "RetentionPercent", "CycleCount", "HealthScore", "HealthCategory", "AlgorithmVersion"],
            (c, r) => c switch
            {
                0 => IsoTime(r, 0),
                6 => EnumName<HealthCategory>(r, 6),
                _ => Scalar(r, c),
            }),

        new(ExportScope.DailyStatistics, "DailyStatistics", "DayUtc",
            ["DayUtc", "ChargeSessions", "DischargeSessions", "ChargingSeconds", "DischargingSeconds", "ScreenOnSeconds", "ScreenOffSeconds", "SleepSeconds", "PercentCharged", "PercentDischarged", "AvgChargeRateMw", "AvgDischargeRateMw", "AvgTemperatureDk", "EndFullChargeMwh", "EndHealthScore"],
            ["DayUtc", "ChargeSessions", "DischargeSessions", "ChargingSeconds", "DischargingSeconds", "ScreenOnSeconds", "ScreenOffSeconds", "SleepSeconds", "PercentCharged", "PercentDischarged", "AvgChargeRateMw", "AvgDischargeRateMw", "AvgTemperatureCelsius", "EndFullChargeMwh", "EndHealthScore"],
            (c, r) => c switch
            {
                0 => IsoTime(r, 0),
                12 => Celsius(r, 12),
                _ => Scalar(r, c),
            }),

        new(ExportScope.ApplicationUsage, "ApplicationUsage", "DayUtc",
            ["DayUtc", "ApplicationKey", "DisplayName", "TotalSeconds", "ForegroundSeconds", "AvgCpuPercent", "EstimatedEnergyMwh", "EstimatorVersion"],
            ["DayUtc", "ApplicationKey", "DisplayName", "TotalSeconds", "ForegroundSeconds", "AvgCpuPercent", "EstimatedEnergyMwh", "EstimatorVersion"],
            (c, r) => c switch
            {
                0 => IsoTime(r, 0),
                _ => Scalar(r, c),
            }),
    ];
}
