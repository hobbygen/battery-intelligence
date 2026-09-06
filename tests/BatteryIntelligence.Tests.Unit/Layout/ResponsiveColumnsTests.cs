using BatteryIntelligence.Core.Layout;

namespace BatteryIntelligence.Tests.Unit.Layout;

/// <summary>
/// The dashboard's responsive-grid breakpoints (docs/ui-navigation.md section 3).
/// Traceability: R-091.
/// </summary>
public sealed class ResponsiveColumnsTests
{
    [Theory]
    [InlineData(320, 1)]
    [InlineData(699, 1)]
    [InlineData(700, 2)]
    [InlineData(960, 2)]
    [InlineData(1099, 2)]
    [InlineData(1100, 3)]
    [InlineData(1366, 3)]
    [InlineData(1599, 3)]
    [InlineData(1600, 4)]
    [InlineData(2560, 4)]
    [InlineData(3840, 4)]
    public void ForWidth_MatchesTheBreakpointTable(double width, int expected) =>
        Assert.Equal(expected, ResponsiveColumns.ForWidth(width));

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ForWidth_DegenerateWidths_YieldOneColumn(double width) =>
        Assert.Equal(1, ResponsiveColumns.ForWidth(width));
}
