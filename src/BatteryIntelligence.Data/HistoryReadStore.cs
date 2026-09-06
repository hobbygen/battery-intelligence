using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.History;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Power;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data;

/// <inheritdoc cref="IHistoryReadStore"/>
public sealed class HistoryReadStore : IHistoryReadStore
{
    // DailyStatistics keeps session aggregates, not a per-metric time series, so a
    // request wide enough to pick the Daily tier reads the Hour tier instead,
    // clamped to the hour tier's 365-day retention window (roadmap.md, Phase 11
    // deviations). The tier caption tells the user which granularity they got.
    private static readonly TimeSpan HourTierWindow = TimeSpan.FromDays(365);

    private readonly ISqliteConnectionFactory _connectionFactory;

    public HistoryReadStore(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<ChartSeries> GetSeriesAsync(HistoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        (string label, string unit) = Describe(request.Metric);
        HistoryTier tier = HistoryTierSelector.TierForSpan(request.Range.Duration);

        DateTimeOffset fromUtc = request.Range.FromUtc;
        DateTimeOffset toUtc = request.Range.ToUtc;
        if (tier == HistoryTier.Daily)
        {
            tier = HistoryTier.Hour;
            DateTimeOffset earliest = toUtc - HourTierWindow;
            if (fromUtc < earliest)
            {
                fromUtc = earliest;
            }
        }

        (string table, string timeColumn, string valueColumn) = Resolve(request.Metric, tier);
        bool temperatureToCelsius = request.Metric == HistoryMetric.TemperatureCelsius;

        var points = new List<TimePoint>();

        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {timeColumn}, {valueColumn} FROM {table} " +
            $"WHERE {timeColumn} >= $from AND {timeColumn} < $to AND {valueColumn} IS NOT NULL " +
            $"ORDER BY {timeColumn};";
        command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", toUtc.ToUnixTimeMilliseconds());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0));
            double value = reader.GetDouble(1);
            if (temperatureToCelsius)
            {
                value = value / 10.0 - 273.15;
            }

            points.Add(new TimePoint(timestamp, value));
        }

        if (points.Count == 0)
        {
            return ChartSeries.Empty(label, unit);
        }

        IReadOnlyList<TimePoint> bounded = MinMaxDownsampler.Downsample(points, request.PointBudget);
        return new ChartSeries(label, unit, bounded);
    }

    public async Task<(DateTimeOffset? EarliestUtc, DateTimeOffset? LatestUtc)> GetExtentAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT MIN(t), MAX(t) FROM (
                SELECT MIN(TimestampUtc) AS t FROM BatterySample
                UNION ALL SELECT MAX(TimestampUtc) FROM BatterySample
                UNION ALL SELECT MIN(HourUtc) FROM SampleHour
                UNION ALL SELECT MAX(HourUtc) FROM SampleHour
            );
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0))
        {
            return (null, null);
        }

        return (
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)));
    }

    public async Task<bool> HasTemperatureDataAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM TemperatureSample LIMIT 1);";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result) != 0;
    }

    private static (string Label, string Unit) Describe(HistoryMetric metric) => metric switch
    {
        HistoryMetric.ChargePercent => ("Charge", "%"),
        HistoryMetric.PowerMw => ("Power", "mW"),
        HistoryMetric.VoltageMv => ("Voltage", "mV"),
        HistoryMetric.TemperatureCelsius => ("Temperature", "°C"),
        _ => ("Value", string.Empty),
    };

    private static (string Table, string TimeColumn, string ValueColumn) Resolve(HistoryMetric metric, HistoryTier tier)
    {
        return tier switch
        {
            HistoryTier.Raw => metric switch
            {
                HistoryMetric.ChargePercent => ("BatterySample", "TimestampUtc", "Percentage"),
                HistoryMetric.PowerMw => ("BatterySample", "TimestampUtc", "PowerMw"),
                HistoryMetric.VoltageMv => ("BatterySample", "TimestampUtc", "VoltageMv"),
                HistoryMetric.TemperatureCelsius => ("TemperatureSample", "TimestampUtc", "TemperatureDk"),
                _ => throw Unsupported(metric),
            },
            HistoryTier.Minute => ("SampleMinute", "MinuteUtc", MinuteColumn(metric)),
            _ => ("SampleHour", "HourUtc", HourColumn(metric)),
        };
    }

    private static string MinuteColumn(HistoryMetric metric) => metric switch
    {
        HistoryMetric.ChargePercent => "AvgPercentage",
        HistoryMetric.PowerMw => "AvgPowerMw",
        HistoryMetric.VoltageMv => "AvgVoltageMv",
        HistoryMetric.TemperatureCelsius => "AvgTemperatureDk",
        _ => throw Unsupported(metric),
    };

    private static string HourColumn(HistoryMetric metric) => metric switch
    {
        HistoryMetric.ChargePercent => "AvgPercentage",
        HistoryMetric.PowerMw => "AvgPowerMw",
        HistoryMetric.VoltageMv => "AvgVoltageMv",
        HistoryMetric.TemperatureCelsius => "AvgTemperatureDk",
        _ => throw Unsupported(metric),
    };

    private static ArgumentOutOfRangeException Unsupported(HistoryMetric metric) =>
        new(nameof(metric), metric, "Unknown history metric.");
}
