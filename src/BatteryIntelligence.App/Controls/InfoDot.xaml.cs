using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Controls;

/// <summary>
/// A small "ⓘ" affordance that explains why a nearby figure is an estimate or a
/// calculated value (specification section 76; R-095). The one sentence in
/// <see cref="Text"/> is both the hover tooltip and the accessible name.
/// </summary>
public sealed partial class InfoDot : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(InfoDot), new PropertyMetadata(string.Empty, OnTextChanged));

    public InfoDot()
    {
        InitializeComponent();
    }

    /// <summary>The provenance sentence shown on hover and read by a screen reader.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        InfoDot dot = (InfoDot)d;
        string text = e.NewValue as string ?? string.Empty;
        dot.Tip.Content = text;
        AutomationProperties.SetName(dot.Icon, text);
    }
}
