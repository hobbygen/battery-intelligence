using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>Battery identity, capacity and health. Specification sections 8-9.</summary>
public sealed partial class BatteryPage : Page
{
    public BatteryPage()
    {
        ViewModel = App.Services.GetRequiredService<BatteryViewModel>();
        InitializeComponent();

        // The view model owns a subscription to the singleton monitoring
        // service; Frame navigation creates a new page (and a new view model) on
        // every visit, so it must be released here or subscriptions accumulate.
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    /// <summary>View model backing this page.</summary>
    public BatteryViewModel ViewModel { get; }
}
