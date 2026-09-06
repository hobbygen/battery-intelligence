using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BatteryIntelligence.App.Controls;

/// <summary>
/// The "data-seg" pill selector from the imported design: a small row of segments
/// where exactly one is active. Used for chart-range and metric-tab selectors.
/// </summary>
public sealed partial class Segmented : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(Segmented),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(Segmented),
        new PropertyMetadata(0, OnSelectedIndexChanged));

    private bool _rebuilding;

    public Segmented()
    {
        InitializeComponent();
    }

    /// <summary>Raised after the user picks a different segment.</summary>
    public event EventHandler<int>? SelectionChanged;

    /// <summary>Labels to show. Strings, or objects whose <c>ToString()</c> / <c>Label</c> is shown.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Segmented)d).Rebuild();

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Segmented)d).ApplySelection();

    private void Rebuild()
    {
        _rebuilding = true;
        SegmentHost.Children.Clear();

        if (ItemsSource is not null)
        {
            int index = 0;
            foreach (object? item in ItemsSource)
            {
                int captured = index;
                ToggleButton segment = new()
                {
                    Content = Describe(item),
                    Style = (Style)Resources["SegmentToggleStyle"],
                };
                segment.Click += (_, _) =>
                {
                    if (SelectedIndex != captured)
                    {
                        SelectedIndex = captured;
                        SelectionChanged?.Invoke(this, captured);
                    }
                    else
                    {
                        ApplySelection();
                    }
                };
                SegmentHost.Children.Add(segment);
                index++;
            }
        }

        _rebuilding = false;
        ApplySelection();
    }

    private void ApplySelection()
    {
        if (_rebuilding)
        {
            return;
        }

        Brush selectedBg = Resource("AppSurfaceBrush");
        Brush selectedFg = Resource("AppTextBrush");
        Brush idleFg = Resource("AppText3Brush");
        Brush transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        for (int i = 0; i < SegmentHost.Children.Count; i++)
        {
            if (SegmentHost.Children[i] is not ToggleButton segment)
            {
                continue;
            }

            bool active = i == SelectedIndex;
            segment.IsChecked = active;
            segment.Background = active ? selectedBg : transparent;
            segment.Foreground = active ? selectedFg : idleFg;
        }
    }

    private Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private static string Describe(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        System.Reflection.PropertyInfo? label = item.GetType().GetProperty("Label");
        return label?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
    }
}
