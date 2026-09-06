using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace BatteryIntelligence.App.Controls;

/// <summary>
/// A small, dependency-free trend line for dashboard cards and inline history
/// previews. Full charts (docs/ui-navigation.md section 6) still go through
/// <see cref="PowerChart"/>.
/// </summary>
public sealed partial class Sparkline : UserControl
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(Sparkline),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(Sparkline),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty BaselineBrushProperty = DependencyProperty.Register(
        nameof(BaselineBrush), typeof(Brush), typeof(Sparkline),
        new PropertyMetadata(null, OnChanged));

    /// <summary>When set, a dashed horizontal reference line at this data value.</summary>
    public static readonly DependencyProperty BaselineValueProperty = DependencyProperty.Register(
        nameof(BaselineValue), typeof(double), typeof(Sparkline),
        new PropertyMetadata(double.NaN, OnChanged));

    public Sparkline()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
        ActualThemeChanged += (_, _) => Render();
    }

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush? BaselineBrush
    {
        get => (Brush?)GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
    }

    public double BaselineValue
    {
        get => (double)GetValue(BaselineValueProperty);
        set => SetValue(BaselineValueProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Sparkline)d).Render();

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        Render();
    }

    private void Render()
    {
        double w = Host.ActualWidth;
        double h = Host.ActualHeight;
        IReadOnlyList<double>? values = Values;

        if (LineBrush is not null)
        {
            LineShape.Stroke = LineBrush;
        }

        if (values is null || values.Count < 2 || w <= 1 || h <= 1)
        {
            LineShape.Points.Clear();
            FillShape.Visibility = Visibility.Collapsed;
            BaselineShape.Visibility = Visibility.Collapsed;
            return;
        }

        double min = values.Min();
        double max = values.Max();
        double range = max - min;
        if (range <= 0)
        {
            range = 1;
            min -= 0.5;
        }

        double pad = 3;
        double usableH = h - (pad * 2);
        double stepX = w / (values.Count - 1);

        double MapY(double v) => pad + (usableH * (1 - ((v - min) / range)));

        PointCollection points = [];
        for (int i = 0; i < values.Count; i++)
        {
            points.Add(new Point(i * stepX, MapY(values[i])));
        }

        LineShape.Points = points;

        if (FillBrush is not null)
        {
            PointCollection area = [.. points];
            area.Add(new Point(w, h));
            area.Add(new Point(0, h));
            FillShape.Points = area;
            FillShape.Fill = FillBrush;
            FillShape.Visibility = Visibility.Visible;
        }
        else
        {
            FillShape.Visibility = Visibility.Collapsed;
        }

        if (!double.IsNaN(BaselineValue) && BaselineBrush is not null)
        {
            double y = MapY(BaselineValue);
            BaselineShape.X1 = 0;
            BaselineShape.X2 = w;
            BaselineShape.Y1 = y;
            BaselineShape.Y2 = y;
            BaselineShape.Stroke = BaselineBrush;
            BaselineShape.Visibility = Visibility.Visible;
        }
        else
        {
            BaselineShape.Visibility = Visibility.Collapsed;
        }
    }
}
