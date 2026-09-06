using BatteryIntelligence.Core.Diagnostics;

namespace BatteryIntelligence.Tests.Unit.Diagnostics;

/// <summary>Parsing the Serilog file-sink output (specification section 26).</summary>
public sealed class LogLineParserTests
{
    private static readonly string[] Sample =
    [
        "2026-09-06 22:40:55.543 +04:00 [INF]  Battery Intelligence 1.0.0.0 starting.",
        "2026-09-06 22:41:01.100 +04:00 [WRN] BatteryIntelligence.Power Power sampling failed for battery 0.",
        "2026-09-06 22:41:10.000 +04:00 [FTL]  Unhandled exception: boom",
        "System.InvalidOperationException: boom",
        "   at Something.Method()",
        "",
        "2026-09-06 22:41:20.000 +04:00 [DBG] X A debug line.",
    ];

    [Fact]
    public void Parse_ReturnsOneEntryPerHeaderLine_WithLevelsNormalised()
    {
        IReadOnlyList<LogEntry> entries = LogLineParser.Parse(Sample);

        Assert.Equal(4, entries.Count);
        Assert.Equal(["INFO", "WARNING", "FATAL", "DEBUG"], entries.Select(e => e.Level));
    }

    [Fact]
    public void Parse_FoldsContinuationLinesIntoThePriorEntry()
    {
        LogEntry fatal = LogLineParser.Parse(Sample).Single(e => e.Level == "FATAL");

        Assert.Contains("Unhandled exception: boom", fatal.Text);
        Assert.Contains("System.InvalidOperationException: boom", fatal.Text);
        Assert.Contains("at Something.Method()", fatal.Text);
    }

    [Fact]
    public void Parse_KeepsTheTimestampWithItsOffset()
    {
        LogEntry first = LogLineParser.Parse(Sample)[0];

        Assert.NotNull(first.TimestampLocal);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 22, 40, 55, 543, TimeSpan.FromHours(4)), first.TimestampLocal!.Value);
    }

    [Fact]
    public void Parse_InputWithNoHeaders_IsEmpty()
    {
        IReadOnlyList<LogEntry> entries = LogLineParser.Parse(["just some text", "   ", "more text"]);

        Assert.Empty(entries);
    }
}
