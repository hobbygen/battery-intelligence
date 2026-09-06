using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Controls;

/// <summary>The kicker row at the top of a content card: glyph + caps label + optional action link.</summary>
public sealed partial class CardHeader : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(CardHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CardHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(CardHeader),
        new PropertyMetadata(string.Empty, OnActionTextChanged));

    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(
        nameof(Info), typeof(string), typeof(CardHeader),
        new PropertyMetadata(string.Empty, OnInfoChanged));

    public CardHeader()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the trailing action link is clicked.</summary>
    public event EventHandler? ActionInvoked;

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>Optional one-sentence explanation shown on an ⓘ tooltip (docs/ui-navigation.md section 4).</summary>
    public string Info
    {
        get => (string)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    private static void OnActionTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        CardHeader header = (CardHeader)d;
        header.ActionButton.Visibility = string.IsNullOrEmpty(e.NewValue as string)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static void OnInfoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        CardHeader header = (CardHeader)d;
        string info = e.NewValue as string ?? string.Empty;
        header.InfoToolTip.Content = info;
        header.InfoIcon.Visibility = string.IsNullOrEmpty(info) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ActionInvoked?.Invoke(this, EventArgs.Empty);
    }
}
