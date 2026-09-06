using BatteryIntelligence.App.Services;
using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>Dashboard. Live overview of battery, power and sessions.</summary>
public sealed partial class DashboardPage : Page
{
    private readonly INavigationService _navigation;

    public DashboardPage()
    {
        Battery = App.Services.GetRequiredService<BatteryViewModel>();
        Power = App.Services.GetRequiredService<PowerViewModel>();
        Sessions = App.Services.GetRequiredService<SessionsViewModel>();
        Temperature = App.Services.GetRequiredService<TemperatureViewModel>();
        AppUsage = App.Services.GetRequiredService<AppUsageViewModel>();
        Insights = App.Services.GetRequiredService<InsightsViewModel>();
        Statistics = App.Services.GetRequiredService<StatisticsViewModel>();
        _navigation = App.Services.GetRequiredService<INavigationService>();
        InitializeComponent();

        // Each view model holds a monitoring subscription for the life of the page;
        // release them all when the page is torn down (see BatteryPage.xaml.cs).
        Unloaded += (_, _) =>
        {
            Battery.Dispose();
            Power.Dispose();
            Sessions.Dispose();
            Temperature.Dispose();
            AppUsage.Dispose();
            Insights.Dispose();
            Statistics.Dispose();
        };
    }

    /// <summary>Battery hero + health.</summary>
    public BatteryViewModel Battery { get; }

    /// <summary>Live electrical figures for the power card.</summary>
    public PowerViewModel Power { get; }

    /// <summary>Current-session summary.</summary>
    public SessionsViewModel Sessions { get; }

    /// <summary>Battery temperature (unavailable on the reference machine).</summary>
    public TemperatureViewModel Temperature { get; }

    /// <summary>Top application by estimated battery impact, for the usage card.</summary>
    public AppUsageViewModel AppUsage { get; }

    /// <summary>Qualifying rule-based insights for the Smart Insights card.</summary>
    public InsightsViewModel Insights { get; }

    /// <summary>Today's aggregate usage for the Statistics card.</summary>
    public StatisticsViewModel Statistics { get; }

    private void OnCardHeaderAction(object sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("Battery");
    }

    private void OnAppUsageCardAction(object sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("AppUsage");
    }

    private void OnStatisticsCardAction(object sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("Statistics");
    }

    private void OnPowerCardAction(object sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("Power");
    }

    private void OnSessionsCardAction(object sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("Sessions");
    }

    private void OnTemperatureCardAction(object sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("Temperature");
    }

    private void OnAlertsCardAction(object sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("Alerts");
    }
}
