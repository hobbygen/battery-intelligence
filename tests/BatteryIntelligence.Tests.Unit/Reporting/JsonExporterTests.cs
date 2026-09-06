using System.Text;
using System.Text.Json;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Reporting;

namespace BatteryIntelligence.Tests.Unit.Reporting;

/// <summary>The JSON shape, escaping and empty-table handling (specification section 36).</summary>
public sealed class JsonExporterTests
{
    private static readonly ExportRequest Request = new(
        new DateRange(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero)),
        ExportScope.All);

    [Fact]
    public async Task ExportAsync_ProducesValidJsonWithRangeAndTables()
    {
        using JsonDocument doc = await RunAsync(
            new ExportTable("BatterySample", ["TimestampUtc", "Percentage"], [["2026-09-02T10:00:00", "80"]]));

        JsonElement root = doc.RootElement;
        Assert.True(root.TryGetProperty("exportedUtc", out _));

        JsonElement range = root.GetProperty("range");
        Assert.Equal("2026-09-01T00:00:00.0000000+00:00", range.GetProperty("fromUtc").GetString());
        Assert.Equal("2026-09-06T00:00:00.0000000+00:00", range.GetProperty("toUtc").GetString());

        JsonElement rows = root.GetProperty("tables").GetProperty("BatterySample");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("80", rows[0].GetProperty("Percentage").GetString());
    }

    [Fact]
    public async Task ExportAsync_EmptyTable_IsAnEmptyArray()
    {
        using JsonDocument doc = await RunAsync(new ExportTable("Alert", ["Id"], []));

        JsonElement rows = doc.RootElement.GetProperty("tables").GetProperty("Alert");
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.Equal(0, rows.GetArrayLength());
    }

    [Fact]
    public async Task ExportAsync_EscapesSpecialCharactersAndPreservesNulls()
    {
        using JsonDocument doc = await RunAsync(
            new ExportTable("T", ["text", "missing"], [["quote \" and \\ and \n", null]]));

        JsonElement row = doc.RootElement.GetProperty("tables").GetProperty("T")[0];
        Assert.Equal("quote \" and \\ and \n", row.GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("missing").ValueKind);
    }

    private static async Task<JsonDocument> RunAsync(params ExportTable[] tables)
    {
        using var stream = new MemoryStream();
        await new JsonExporter().ExportAsync(Request, tables, stream, CancellationToken.None);
        return JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
