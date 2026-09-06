using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>Temperature. Battery thermal monitoring, per-band breakdown and thresholds.</summary>
public sealed partial class TemperaturePage : Page
{
    public TemperaturePage()
    {
        ViewModel = App.Services.GetRequiredService<TemperatureViewModel>();
        InitializeComponent();

        // The view model holds a monitoring subscription for the life of the page;
        // release it on teardown (see BatteryPage.xaml.cs).
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    /// <summary>Backs the Temperature page.</summary>
    public TemperatureViewModel ViewModel { get; }
}
