using BatteryIntelligence.App.Services;
using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>App Usage. Estimated per-application battery impact (AppEnergyV1).</summary>
public sealed partial class AppUsagePage : Page
{
    private readonly INavigationService _navigation;

    public AppUsagePage()
    {
        ViewModel = App.Services.GetRequiredService<AppUsageViewModel>();
        _navigation = App.Services.GetRequiredService<INavigationService>();
        InitializeComponent();

        // See BatteryPage.xaml.cs: release the monitoring subscription when the
        // page is torn down, or repeated navigation would accumulate it.
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    public AppUsageViewModel ViewModel { get; }

    // The AppEnergyV1 methodology is reproduced in full on the About page
    // (specification section 55). CardHeader raises a plain EventHandler; the
    // HyperlinkButton raises a RoutedEventHandler — hence the two shims.
    private void OnMethodologyAction(object? sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("About");
    }

    private void OnMethodologyClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _navigation.NavigateTo("About");
    }
}
