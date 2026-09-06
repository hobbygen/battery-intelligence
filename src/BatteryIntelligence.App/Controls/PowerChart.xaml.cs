using System.Globalization;
using System.Linq;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;

namespace BatteryIntelligence.App.Controls;

/// <summary>
/// A single synchronised line chart for one <see cref="ChartSeries"/>
/// (docs/ui-navigation.md section 6). Points are already bounded and
/// min/max-downsampled upstream, so this control only adapts them to the
/// charting library and keeps the X axis pinned to the shared window.
/// </summary>
public sealed partial class PowerChart : UserControl
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(ChartSeries), typeof(PowerChart),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty RangeStartUtcProperty = DependencyProperty.Register(
        nameof(RangeStartUtc), typeof(DateTimeOffset), typeof(PowerChart),
        new PropertyMetadata(DateTimeOffset.MinValue, OnChanged));

    public static readonly DependencyProperty RangeEndUtcProperty = DependencyProperty.Register(
        nameof(RangeEndUtc), typeof(DateTimeOffset), typeof(PowerChart),
        new PropertyMetadata(DateTimeOffset.MinValue, OnChanged));

    /// <summary>The grade badge to show beside the title, or null/empty for none.</summary>
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(
        nameof(Badge), typeof(string), typeof(PowerChart),
        new PropertyMetadata(string.Empty, OnChanged));

    /// <summary>Which accent the line uses: "accent" (default) or "magenta" (temperature).</summary>
    public static readonly DependencyProperty LineAccentProperty = DependencyProperty.Register(
        nameof(LineAccent), typeof(string), typeof(PowerChart),
        new PropertyMetadata("accent", OnChanged));

    /// <summary>When not NaN, a dashed warning threshold line plus a tinted band above it.</summary>
    public static readonly DependencyProperty ThresholdValueProperty = DependencyProperty.Register(
        nameof(ThresholdValue), typeof(double), typeof(PowerChart),
        new PropertyMetadata(double.NaN, OnChanged));

    /// <summary>Label drawn on the threshold line (e.g. "Warning 45 °C").</summary>
    public static readonly DependencyProperty ThresholdLabelProperty = DependencyProperty.Register(
        nameof(ThresholdLabel), typeof(string), typeof(PowerChart),
        new PropertyMetadata(string.Empty, OnChanged));

    private readonly CartesianChart _chart;

    public PowerChart()
    {
        InitializeComponent();

        _chart = new CartesianChart
        {
            TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Top,
            AnimationsSpeed = TimeSpan.Zero,
        };
        ChartHost.Content = _chart;

        ActualThemeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    public ChartSeries? Series
    {
        get => (ChartSeries?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public DateTimeOffset RangeStartUtc
    {
        get => (DateTimeOffset)GetValue(RangeStartUtcProperty);
        set => SetValue(RangeStartUtcProperty, value);
    }

    public DateTimeOffset RangeEndUtc
    {
        get => (DateTimeOffset)GetValue(RangeEndUtcProperty);
        set => SetValue(RangeEndUtcProperty, value);
    }

    public string Badge
    {
        get => (string)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public string LineAccent
    {
        get => (string)GetValue(LineAccentProperty);
        set => SetValue(LineAccentProperty, value);
    }

    public double ThresholdValue
    {
        get => (double)GetValue(ThresholdValueProperty);
        set => SetValue(ThresholdValueProperty, value);
    }

    public string ThresholdLabel
    {
        get => (string)GetValue(ThresholdLabelProperty);
        set => SetValue(ThresholdLabelProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PowerChart)d).Render();

    private void Render()
    {
        ChartSeries? series = Series;
        TitleText.Text = series is null ? string.Empty : $"{series.Label} ({series.Unit})";

        bool hasBadge = !string.IsNullOrEmpty(Badge);
        BadgeBorder.Visibility = hasBadge ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = Badge;

        bool hasPoints = series is { Points.Count: > 1 };
        EmptyText.Visibility = hasPoints ? Visibility.Collapsed : Visibility.Visible;
        _chart.Visibility = hasPoints ? Visibility.Visible : Visibility.Collapsed;

        if (!hasPoints)
        {
            _chart.Series = [];
            AutomationProperties.SetName(this, $"{series?.Label ?? "Chart"}: not enough data yet.");
            return;
        }

        bool dark = ActualTheme == ElementTheme.Dark;

        SKColor stroke = string.Equals(LineAccent, "magenta", StringComparison.OrdinalIgnoreCase)
            ? (dark ? new SKColor(0xFF, 0x7A, 0xAB) : new SKColor(0xD6, 0x00, 0x6C))
            : (dark ? new SKColor(0x57, 0xBC, 0xDC) : new SKColor(0x00, 0x88, 0xB0));

        SKColor warn = dark ? new SKColor(0xDD, 0xA6, 0x3F) : new SKColor(0x8A, 0x5A, 0x00);

        SKColor axisText = dark
            ? new SKColor(0xA9, 0xA4, 0xA1)
            : new SKColor(0x60, 0x5D, 0x5D);

        IReadOnlyList<TimePoint> points = series!.Points;
        DateTimePoint[] values = [.. points.Select(p => new DateTimePoint(p.TimestampUtc.LocalDateTime, p.Value))];

        _chart.Series =
        [
            new LineSeries<DateTimePoint>
            {
                Values = values,
                Name = series.Label,
                GeometrySize = 0,
                LineSmoothness = 0,
                Stroke = new SolidColorPaint(stroke, 2),
                Fill = null,
                XToolTipLabelFormatter = point => new DateTime((long)point.Coordinate.SecondaryValue).ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                YToolTipLabelFormatter = point => $"{point.Coordinate.PrimaryValue:N0} {series.Unit}",
            },
        ];

        DateTimeOffset start = RangeStartUtc == DateTimeOffset.MinValue ? points[0].TimestampUtc : RangeStartUtc;
        DateTimeOffset end = RangeEndUtc == DateTimeOffset.MinValue ? points[^1].TimestampUtc : RangeEndUtc;

        _chart.XAxes =
        [
            new Axis
            {
                Labeler = value => TicksToLabel(value),
                UnitWidth = TimeSpan.FromSeconds(1).Ticks,
                MinLimit = start.LocalDateTime.Ticks,
                MaxLimit = end.LocalDateTime.Ticks,
                TextSize = 11,
                LabelsPaint = new SolidColorPaint(axisText),
                SeparatorsPaint = new SolidColorPaint(axisText.WithAlpha(40)),
            },
        ];

        _chart.YAxes =
        [
            new Axis
            {
                Labeler = value => value.ToString("N0", CultureInfo.CurrentCulture),
                TextSize = 11,
                LabelsPaint = new SolidColorPaint(axisText),
                SeparatorsPaint = new SolidColorPaint(axisText.WithAlpha(40)),
            },
        ];

        if (!double.IsNaN(ThresholdValue))
        {
            _chart.Sections =
            [
                new RectangularSection
                {
                    Yi = ThresholdValue,
                    Fill = new SolidColorPaint(warn.WithAlpha(28)),
                },
                new RectangularSection
                {
                    Yi = ThresholdValue,
                    Yj = ThresholdValue,
                    Stroke = new SolidColorPaint(warn, 1.5f)
                    {
                        PathEffect = new DashEffect([6, 6]),
                    },
                    Label = ThresholdLabel,
                    LabelPaint = new SolidColorPaint(warn) { SKTypeface = SKTypeface.Default },
                    LabelSize = 11,
                },
            ];
        }
        else
        {
            _chart.Sections = [];
        }

        double min = points.Min(p => p.Value);
        double max = points.Max(p => p.Value);
        AutomationProperties.SetName(
            this,
            $"{series.Label} chart in {series.Unit}. {points.Count} points from {start.LocalDateTime:HH:mm:ss} to {end.LocalDateTime:HH:mm:ss}. " +
            $"Range {min:N0} to {max:N0} {series.Unit}.");
    }

    private static string TicksToLabel(double value)
    {
        long ticks = (long)value;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return string.Empty;
        }

        return new DateTime(ticks).ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }
}
