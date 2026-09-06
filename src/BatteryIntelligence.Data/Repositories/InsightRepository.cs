using System.Text.Json;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// Reads and writes <c>Insight</c> rows (docs/database.md; specification
/// section 18). The active set is replaced wholesale each analytics pass;
/// dismissed rows are kept so a suppressed insight does not reappear.
/// </summary>
internal sealed class InsightRepository
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public async Task ReplaceActiveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<AnalyticsInsight> insights,
        long nowUtcMs,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM Insight WHERE Dismissed = 0;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (insights.Count == 0)
        {
            return;
        }

        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Insight
                (GeneratedUtc, InsightType, Severity, Title, Explanation, SupportingJson,
                 Confidence, PeriodStartUtc, PeriodEndUtc, RuleVersion, Dismissed)
            VALUES
                ($generated, $type, $severity, $title, $explanation, $supporting,
                 $confidence, $periodStart, $periodEnd, $ruleVersion, 0);
            """;

        SqliteParameter generated = insert.Parameters.Add("$generated", SqliteType.Integer);
        SqliteParameter type = insert.Parameters.Add("$type", SqliteType.Integer);
        SqliteParameter severity = insert.Parameters.Add("$severity", SqliteType.Integer);
        SqliteParameter title = insert.Parameters.Add("$title", SqliteType.Text);
        SqliteParameter explanation = insert.Parameters.Add("$explanation", SqliteType.Text);
        SqliteParameter supporting = insert.Parameters.Add("$supporting", SqliteType.Text);
        SqliteParameter confidence = insert.Parameters.Add("$confidence", SqliteType.Real);
        SqliteParameter periodStart = insert.Parameters.Add("$periodStart", SqliteType.Integer);
        SqliteParameter periodEnd = insert.Parameters.Add("$periodEnd", SqliteType.Integer);
        SqliteParameter ruleVersion = insert.Parameters.Add("$ruleVersion", SqliteType.Text);

        await insert.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (AnalyticsInsight i in insights)
        {
            generated.Value = nowUtcMs;
            type.Value = (int)i.Type;
            severity.Value = (int)i.Severity;
            title.Value = i.Title;
            explanation.Value = i.Explanation;
            supporting.Value = JsonSerializer.Serialize(i.Supporting, Json);
            confidence.Value = i.Confidence;
            periodStart.Value = (object?)i.PeriodStartUtc?.ToUnixTimeMilliseconds() ?? DBNull.Value;
            periodEnd.Value = (object?)i.PeriodEndUtc?.ToUnixTimeMilliseconds() ?? DBNull.Value;
            ruleVersion.Value = i.RuleVersion;

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<AnalyticsInsight>> GetActiveAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, InsightType, Severity, Title, Explanation, SupportingJson,
                   Confidence, PeriodStartUtc, PeriodEndUtc, RuleVersion
            FROM Insight
            WHERE Dismissed = 0
            ORDER BY Severity DESC, Confidence DESC, GeneratedUtc DESC;
            """;

        List<AnalyticsInsight> insights = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            IReadOnlyDictionary<string, double> supporting = reader.IsDBNull(5)
                ? new Dictionary<string, double>()
                : JsonSerializer.Deserialize<Dictionary<string, double>>(reader.GetString(5), Json) ?? [];

            insights.Add(new AnalyticsInsight(
                (InsightType)reader.GetInt32(1),
                (InsightSeverity)reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                supporting,
                reader.GetDouble(6),
                reader.IsDBNull(7) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
                reader.IsDBNull(8) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8)),
                reader.GetString(9))
            {
                Id = reader.GetInt64(0),
            });
        }

        return insights;
    }

    public async Task DismissAsync(SqliteConnection connection, long insightId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Insight SET Dismissed = 1 WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", insightId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
