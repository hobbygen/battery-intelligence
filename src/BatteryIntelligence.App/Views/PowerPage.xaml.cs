using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>Power. Live electrical measurements over a selectable window.</summary>
public sealed partial class PowerPage : Page
{
    public PowerPage()
    {
        ViewModel = App.Services.GetRequiredService<PowerViewModel>();
        InitializeComponent();

        // See BatteryPage.xaml.cs: release the monitoring subscription when the
        // page is torn down, or repeated navigation would accumulate it.
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    public PowerViewModel ViewModel { get; }
}
