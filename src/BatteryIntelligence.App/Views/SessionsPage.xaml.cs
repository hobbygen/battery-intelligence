using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>Current and historical charging and discharging sessions.</summary>
public sealed partial class SessionsPage : Page
{
    public SessionsPage()
    {
        ViewModel = App.Services.GetRequiredService<SessionsViewModel>();
        InitializeComponent();

        // See BatteryPage.xaml.cs: release the monitoring subscription when the
        // page is torn down, or repeated navigation would accumulate it.
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    public SessionsViewModel ViewModel { get; }
}
