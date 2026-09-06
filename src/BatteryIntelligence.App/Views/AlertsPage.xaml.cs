using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>Alerts. Per-alert configuration, active alerts and alert history.</summary>
public sealed partial class AlertsPage : Page
{
    public AlertsPage()
    {
        ViewModel = App.Services.GetRequiredService<AlertsViewModel>();
        InitializeComponent();

        Unloaded += (_, _) => ViewModel.Dispose();
    }

    public AlertsViewModel ViewModel { get; }
}
