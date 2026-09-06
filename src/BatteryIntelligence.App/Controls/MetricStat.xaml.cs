using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BatteryIntelligence.App.Controls;

/// <summary>A label / value / unit / grade-badge tile, the repeated element of the hero split cards.</summary>
public sealed partial class MetricStat : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(MetricStat), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(MetricStat), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(MetricStat),
        new PropertyMetadata(string.Empty, OnUnitChanged));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(MetricStat),
        new PropertyMetadata(string.Empty, OnCaptionChanged));

    public static readonly DependencyProperty BadgeTextProperty = DependencyProperty.Register(
        nameof(BadgeText), typeof(string), typeof(MetricStat),
        new PropertyMetadata(string.Empty, OnBadgeChanged));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(MetricStat),
        new PropertyMetadata(null, OnValueBrushChanged));

    public static readonly DependencyProperty InfoTextProperty = DependencyProperty.Register(
        nameof(InfoText), typeof(string), typeof(MetricStat),
        new PropertyMetadata(string.Empty, OnInfoTextChanged));

    public MetricStat()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public Brush? ValueBrush
    {
        get => (Brush?)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    /// <summary>One sentence of provenance shown on an ⓘ hover beside the value (R-095). Empty hides it.</summary>
    public string InfoText
    {
        get => (string)GetValue(InfoTextProperty);
        set => SetValue(InfoTextProperty, value);
    }

    private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricStat)d).UnitText.Visibility = string.IsNullOrEmpty(e.NewValue as string)
            ? Visibility.Collapsed : Visibility.Visible;

    private static void OnCaptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricStat)d).CaptionText.Visibility = string.IsNullOrEmpty(e.NewValue as string)
            ? Visibility.Collapsed : Visibility.Visible;

    private static void OnBadgeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricStat)d).BadgeBorder.Visibility = string.IsNullOrEmpty(e.NewValue as string)
            ? Visibility.Collapsed : Visibility.Visible;

    private static void OnInfoTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var stat = (MetricStat)d;
        string text = e.NewValue as string ?? string.Empty;
        stat.Info.Text = text;
        stat.Info.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnValueBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is Brush brush)
        {
            ((MetricStat)d).ValueText.Foreground = brush;
        }
    }
}
