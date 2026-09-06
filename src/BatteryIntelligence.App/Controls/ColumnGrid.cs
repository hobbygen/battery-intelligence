using BatteryIntelligence.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace BatteryIntelligence.App.Controls;

/// <summary>
/// A responsive masonry panel for the dashboard cards (docs/ui-navigation.md
/// section 3). It arranges its children into 1–4 equal-width columns chosen by the
/// available width, placing each child in the currently shortest column so the
/// cards reflow without their text ever shrinking.
/// </summary>
/// <remarks>
/// Deliberately a hand-written <see cref="Panel"/> rather than
/// <c>ItemsRepeater</c> + <c>UniformGridLayout</c> or a third-party
/// <c>WrapPanel</c>: the cards are heterogeneous hand-authored XAML, not a
/// templated collection. <see cref="ColumnsForWidth"/> is the pure,
/// unit-tested part; the arrange maths is verified live.
/// </remarks>
public sealed partial class ColumnGrid : Panel
{
    /// <summary>The widest a single column is allowed to grow when the panel is measured with unbounded width.</summary>
    private const double MaxColumnWidth = 460.0;

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing), typeof(double), typeof(ColumnGrid),
        new PropertyMetadata(20.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(ColumnGrid),
        new PropertyMetadata(20.0, OnLayoutPropertyChanged));

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>The column count for a given panel width (see <see cref="ResponsiveColumns.ForWidth"/>).</summary>
    public static int ColumnsForWidth(double width) => ResponsiveColumns.ForWidth(width);

    protected override Size MeasureOverride(Size availableSize)
    {
        double panelWidth = availableSize.Width;
        bool unbounded = double.IsInfinity(panelWidth) || double.IsNaN(panelWidth);

        int columns = unbounded ? 4 : ColumnsForWidth(panelWidth);
        columns = Math.Min(columns, Math.Max(1, Children.Count));

        double columnWidth = unbounded
            ? MaxColumnWidth
            : Math.Max(1.0, (panelWidth - (ColumnSpacing * (columns - 1))) / columns);

        double[] columnHeights = new double[columns];
        int[] columnOf = new int[Children.Count];

        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];
            child.Measure(new Size(columnWidth, double.PositiveInfinity));

            int shortest = ShortestColumn(columnHeights);
            columnOf[i] = shortest;
            columnHeights[shortest] += child.DesiredSize.Height + RowSpacing;
        }

        _columnWidth = columnWidth;
        _columns = columns;
        _columnOf = columnOf;

        double totalWidth = unbounded
            ? (columnWidth * columns) + (ColumnSpacing * (columns - 1))
            : panelWidth;
        double totalHeight = 0;
        foreach (double h in columnHeights)
        {
            totalHeight = Math.Max(totalHeight, h);
        }

        totalHeight = Math.Max(0, totalHeight - RowSpacing); // no trailing gap
        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = _columns;
        double columnWidth = _columnWidth;

        // Re-derive column width against the final size if it changed between passes.
        if (!double.IsInfinity(finalSize.Width) && finalSize.Width > 0)
        {
            columns = Math.Min(ColumnsForWidth(finalSize.Width), Math.Max(1, Children.Count));
            columnWidth = Math.Max(1.0, (finalSize.Width - (ColumnSpacing * (columns - 1))) / columns);
        }

        double[] columnHeights = new double[columns];

        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];
            int col = i < _columnOf.Length && _columnOf[i] < columns ? _columnOf[i] : ShortestColumn(columnHeights);

            double x = col * (columnWidth + ColumnSpacing);
            double y = columnHeights[col];
            child.Arrange(new Rect(x, y, columnWidth, child.DesiredSize.Height));
            columnHeights[col] += child.DesiredSize.Height + RowSpacing;
        }

        return finalSize;
    }

    private double _columnWidth = MaxColumnWidth;
    private int _columns = 1;
    private int[] _columnOf = [];

    private static int ShortestColumn(double[] heights)
    {
        int index = 0;
        for (int c = 1; c < heights.Length; c++)
        {
            if (heights[c] < heights[index])
            {
                index = c;
            }
        }

        return index;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColumnGrid)d).InvalidateMeasure();
}
