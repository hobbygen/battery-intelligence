namespace BatteryIntelligence.Core.Layout;

/// <summary>
/// The responsive column count for a container width (docs/ui-navigation.md
/// section 3). Pure so the breakpoints are unit-tested without a UI thread; the
/// dashboard's <c>ColumnGrid</c> panel is the only caller.
/// </summary>
public static class ResponsiveColumns
{
    /// <summary>
    /// Columns for <paramref name="width"/> in device-independent pixels:
    /// 1 below 700, 2 below 1100, 3 below 1600, otherwise 4. A non-positive or
    /// non-finite width yields 1.
    /// </summary>
    public static int ForWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return 1;
        }

        return width switch
        {
            < 700 => 1,
            < 1100 => 2,
            < 1600 => 3,
            _ => 4,
        };
    }
}
