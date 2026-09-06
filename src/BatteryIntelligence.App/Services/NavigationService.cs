using BatteryIntelligence.App.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace BatteryIntelligence.App.Services;

/// <summary>
/// Frame-based implementation of <see cref="INavigationService"/>.
/// </summary>
public sealed class NavigationService : INavigationService
{
    /// <summary>
    /// Tag to page mapping. The tags are also the values stored in settings for
    /// the default page, so they are part of the persisted contract and should
    /// not be renamed casually.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Type> Pages = new Dictionary<string, Type>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = typeof(DashboardPage),
        ["Battery"] = typeof(BatteryPage),
        ["Sessions"] = typeof(SessionsPage),
        ["Power"] = typeof(PowerPage),
        ["Temperature"] = typeof(TemperaturePage),
        ["AppUsage"] = typeof(AppUsagePage),
        ["Statistics"] = typeof(StatisticsPage),
        ["History"] = typeof(HistoryPage),
        ["Alerts"] = typeof(AlertsPage),
        ["Settings"] = typeof(SettingsPage),
        ["Diagnostics"] = typeof(DiagnosticsPage),
        ["About"] = typeof(AboutPage),
    };

    private readonly ILogger<NavigationService> _logger;
    private Frame? _frame;

    public NavigationService(ILogger<NavigationService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public string? CurrentTag { get; private set; }

    /// <inheritdoc/>
    public bool CanGoBack => _frame?.CanGoBack ?? false;

    /// <inheritdoc/>
    public event EventHandler<string>? Navigated;

    /// <summary>Tags of all registered pages, in declaration order.</summary>
    public static IEnumerable<string> KnownTags => Pages.Keys;

    /// <inheritdoc/>
    public void Initialize(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
    }

    /// <inheritdoc/>
    public bool NavigateTo(string tag)
    {
        if (_frame is null)
        {
            _logger.LogWarning("Navigation to {Tag} requested before the frame was set.", tag);
            return false;
        }

        if (string.IsNullOrWhiteSpace(tag) || !Pages.TryGetValue(tag, out Type? pageType))
        {
            _logger.LogWarning("Unknown navigation tag {Tag}.", tag);
            return false;
        }

        if (_frame.Content?.GetType() == pageType)
        {
            return false;
        }

        bool navigated = _frame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
        if (!navigated)
        {
            _logger.LogError("Frame refused navigation to {Tag}.", tag);
            return false;
        }

        // Canonicalise to the dictionary's spelling so listeners always see the
        // same casing regardless of how the caller spelled the tag.
        CurrentTag = Pages.Keys.First(k => string.Equals(k, tag, StringComparison.OrdinalIgnoreCase));
        Navigated?.Invoke(this, CurrentTag);
        return true;
    }

    /// <inheritdoc/>
    public bool GoBack()
    {
        if (_frame is null || !_frame.CanGoBack)
        {
            return false;
        }

        _frame.GoBack();

        Type? current = _frame.Content?.GetType();
        foreach (KeyValuePair<string, Type> entry in Pages)
        {
            if (entry.Value == current)
            {
                CurrentTag = entry.Key;
                Navigated?.Invoke(this, entry.Key);
                break;
            }
        }

        return true;
    }
}
