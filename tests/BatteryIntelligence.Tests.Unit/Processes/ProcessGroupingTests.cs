using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Processes;

namespace BatteryIntelligence.Tests.Unit.Processes;

/// <summary>
/// Intelligent process grouping folds many processes into one application
/// (specification section 15). Traceability: R-051.
/// </summary>
public sealed class ProcessGroupingTests
{
    [Fact]
    public void BrowserRendererAndGpuChildren_CollapseToOneApplication()
    {
        ProcessRawSample tab = Sample("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        ProcessRawSample renderer = Sample("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        ProcessRawSample gpu = Sample("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");

        (string keyA, string nameA) = ProcessGrouping.Resolve(tab, ProcessGroupingTable.Default);
        (string keyB, _) = ProcessGrouping.Resolve(renderer, ProcessGroupingTable.Default);
        (string keyC, _) = ProcessGrouping.Resolve(gpu, ProcessGroupingTable.Default);

        Assert.Equal("chrome", keyA);
        Assert.Equal("Google Chrome", nameA);
        Assert.Equal(keyA, keyB);
        Assert.Equal(keyA, keyC);
    }

    [Fact]
    public void ProcessNameAlone_MatchesWhenThePathIsUnavailable()
    {
        ProcessRawSample slack = Sample("Slack", executablePath: null);

        (string key, string name) = ProcessGrouping.Resolve(slack, ProcessGroupingTable.Default);

        Assert.Equal("slack", key);
        Assert.Equal("Slack", name);
    }

    [Fact]
    public void AnUnrecognisedApplication_FallsBackToItsOwnProcessName()
    {
        ProcessRawSample a = Sample("MyTool", @"C:\tools\MyTool.exe");
        ProcessRawSample b = Sample("MyTool", @"C:\tools\MyTool.exe");

        (string keyA, string nameA) = ProcessGrouping.Resolve(a, ProcessGroupingTable.Default);
        (string keyB, _) = ProcessGrouping.Resolve(b, ProcessGroupingTable.Default);

        Assert.Equal("mytool", keyA);
        Assert.Equal("Mytool", nameA);
        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void APathFragmentRule_WinsOverAProcessNameThatWouldMatchSomethingElse()
    {
        ProcessGroupingTable table = new(
        [
            new ProcessGroupRule("acme", "Acme Suite", [], [@"\acme\"]),
            .. ProcessGroupingTable.Default.Rules,
        ]);

        ProcessRawSample helper = Sample("chrome", @"C:\Program Files\Acme\chrome.exe");

        (string key, string name) = ProcessGrouping.Resolve(helper, table);

        Assert.Equal("acme", key);
        Assert.Equal("Acme Suite", name);
    }

    private static ProcessRawSample Sample(string name, string? executablePath) => new(
        ProcessId: Random.Shared.Next(1, 100_000),
        StartTimeUtc: DateTimeOffset.UnixEpoch,
        ProcessName: name,
        ExecutablePath: executablePath,
        ProcessorTime: TimeSpan.Zero,
        WorkingSetBytes: 0,
        IsForeground: false);
}
