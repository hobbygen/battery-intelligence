using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>One rendered insight row for the Dashboard "Smart Insights" card.</summary>
public sealed record InsightRow(string Title, string Explanation, string ConfidenceText, string SeverityGlyph, Brush AccentBrush);

/// <summary>
/// Backs the Dashboard "Smart Insights" card (specification section 18). Shows the
/// insights that currently qualify, or an honest "nothing meets the confidence and
/// effect-size threshold" state — never padded out.
/// </summary>
public sealed partial class InsightsViewModel : ObservableObject, IDisposable
{
    private readonly IAnalyticsService _analytics;
    private readonly DispatcherQueue _dispatcher;

    private IReadOnlyList<InsightRow> _insights = [];

    public InsightsViewModel(IAnalyticsService analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);

        _analytics = analytics;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _analytics.Updated += OnUpdated;
        Apply();
    }

    public IReadOnlyList<InsightRow> Insights
    {
        get => _insights;
        private set
        {
            if (SetProperty(ref _insights, value))
            {
                OnPropertyChanged(nameof(HasInsights));
                OnPropertyChanged(nameof(NoInsights));
            }
        }
    }

    public bool HasInsights => _insights.Count > 0;

    public bool NoInsights => _insights.Count == 0;

    private void OnUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(Apply);
    }

    private void Apply()
    {
        Insights =
        [
            .. _analytics.Insights.Select(i => new InsightRow(
                i.Title,
                i.Explanation,
                string.Create(CultureInfo.InvariantCulture, $"{i.Confidence * 100:F0}% confidence"),
                SeverityGlyph(i.Severity),
                AccentBrush(i.Severity))),
        ];
    }

    private static string SeverityGlyph(InsightSeverity severity) => severity switch
    {
        InsightSeverity.Warning => "",
        InsightSeverity.Advice => "",
        _ => "",
    };

    private static Brush AccentBrush(InsightSeverity severity)
    {
        string key = severity switch
        {
            InsightSeverity.Warning => "AppWarnBrush",
            InsightSeverity.Advice => "AppAccentBrush",
            _ => "AppText3Brush",
        };

        return Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public void Dispose() => _analytics.Updated -= OnUpdated;
}
