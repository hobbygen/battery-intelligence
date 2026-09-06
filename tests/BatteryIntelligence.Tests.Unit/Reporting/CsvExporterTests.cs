using System.Text;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Reporting;

namespace BatteryIntelligence.Tests.Unit.Reporting;

/// <summary>RFC 4180 quoting, the BOM, line endings and multi-table layout (specification section 36).</summary>
public sealed class CsvExporterTests
{
    private static readonly ExportRequest Request = new(
        new DateRange(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero)),
        ExportScope.All);

    [Fact]
    public async Task ExportAsync_WritesAUtf8BomAndCrlfLineEndings()
    {
        byte[] bytes = await RunAsync(new ExportTable("T", ["A"], [["1"]]));

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        string text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_QuotesFieldsWithCommasQuotesNewlinesAndEdgeSpaces()
    {
        var table = new ExportTable(
            "Tricky",
            ["plain", "comma", "quote", "newline", "spaced"],
            [["ok", "a,b", "she said \"hi\"", "line1\nline2", " padded "]]);

        string text = Encoding.UTF8.GetString(await RunAsync(table));
        string dataLine = SplitLines(text).Last(l => l.Length > 0);

        Assert.Equal("ok,\"a,b\",\"she said \"\"hi\"\"\",\"line1\nline2\",\" padded \"", dataLine);
    }

    [Fact]
    public async Task ExportAsync_SeparatesTablesWithABlankLineAndNamesEach()
    {
        byte[] bytes = await RunAsync(
            new ExportTable("First", ["A"], [["1"]]),
            new ExportTable("Second", ["B"], [["2"]]));

        string text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("# First", text, StringComparison.Ordinal);
        Assert.Contains("# Second", text, StringComparison.Ordinal);
        Assert.Contains("\r\n\r\n# Second", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_EmptyTable_WritesHeaderRowOnly()
    {
        string text = Encoding.UTF8.GetString(await RunAsync(new ExportTable("Empty", ["A", "B"], [])));
        string[] lines = SplitLines(text);

        Assert.Contains("A,B", lines);
        Assert.DoesNotContain(lines, l => l is "1" or "2");
    }

    private static async Task<byte[]> RunAsync(params ExportTable[] tables)
    {
        using var stream = new MemoryStream();
        await new CsvExporter().ExportAsync(Request, tables, stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static string[] SplitLines(string text) =>
        text.TrimStart('﻿').Split("\r\n", StringSplitOptions.None);
}
