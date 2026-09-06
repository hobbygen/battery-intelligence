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

    private void OnViewBatteryDetailsClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("Battery");
    }

    private void OnCardHeaderAction(object sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("Battery");
    }
}
