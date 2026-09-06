using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Controls;

/// <summary>
/// Explains why a region has nothing to show.
/// </summary>
/// <remarks>
/// Specification section 43 requires that empty and unavailable states state the
/// reason rather than simply rendering nothing. <see cref="Description"/> is
/// therefore treated as required content, not decoration: a state that cannot
/// explain itself should not use this control.
/// </remarks>
public sealed partial class EmptyStateView : UserControl
{
    /// <summary>Identifies the <see cref="Glyph"/> property.</summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EmptyStateView), new PropertyMetadata(""));

    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyStateView), new PropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="Description"/> property.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(EmptyStateView), new PropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="ActionText"/> property.</summary>
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(EmptyStateView),
        new PropertyMetadata(string.Empty, OnActionTextChanged));

    /// <summary>Identifies the <see cref="ShowValueDash"/> property.</summary>
    public static readonly DependencyProperty ShowValueDashProperty = DependencyProperty.Register(
        nameof(ShowValueDash), typeof(bool), typeof(EmptyStateView),
        new PropertyMetadata(false, OnShowValueDashChanged));

    public EmptyStateView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the optional action button is clicked.</summary>
    public event EventHandler? ActionInvoked;

    /// <summary>Segoe Fluent Icons glyph shown above the message.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Short statement of what is absent.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Explanation of why it is absent, and what would change it.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Optional action button label; hidden when empty.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>Whether to render a large em dash where a value would be — used for the "sensor unavailable" state.</summary>
    public bool ShowValueDash
    {
        get => (bool)GetValue(ShowValueDashProperty);
        set => SetValue(ShowValueDashProperty, value);
    }

    private static void OnActionTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EmptyStateView)d).ActionBtn.Visibility = string.IsNullOrEmpty(e.NewValue as string)
            ? Visibility.Collapsed : Visibility.Visible;

    private static void OnShowValueDashChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EmptyStateView)d).DashText.Visibility = e.NewValue is true ? Visibility.Visible : Visibility.Collapsed;

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ActionInvoked?.Invoke(this, EventArgs.Empty);
    }
}
