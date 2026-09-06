using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Controls;

/// <summary>A labelled settings toggle: header + description on the left, a <see cref="ToggleSwitch"/> on the right.</summary>
public sealed partial class ToggleRow : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(ToggleRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ToggleRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(ToggleRow), new PropertyMetadata(false, OnIsOnChanged));

    private bool _updatingFromSwitch;

    public ToggleRow()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ToggleRow row = (ToggleRow)d;
        if (row._updatingFromSwitch)
        {
            return;
        }

        row.Switch.IsOn = (bool)e.NewValue;
    }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        _updatingFromSwitch = true;
        IsOn = Switch.IsOn;
        _updatingFromSwitch = false;
    }
}
