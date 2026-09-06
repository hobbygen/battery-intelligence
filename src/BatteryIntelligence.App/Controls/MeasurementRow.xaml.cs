using BatteryIntelligence.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Controls;

/// <summary>One "Label: Value [Badge]" line, driven by a formatted <see cref="DisplayValue"/>.</summary>
public sealed partial class MeasurementRow : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(MeasurementRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(DisplayValue), typeof(MeasurementRow),
        new PropertyMetadata(DisplayValue.Unavailable("Not available")));

    public MeasurementRow()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public DisplayValue Value
    {
        get => (DisplayValue)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
