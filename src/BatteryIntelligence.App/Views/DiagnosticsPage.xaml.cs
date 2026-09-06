using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>
/// Hardware and API availability, monitoring status and diagnostics.
/// Specification sections 26 and 47.
/// </summary>
public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsPage()
    {
        ViewModel = App.Services.GetRequiredService<DiagnosticsViewModel>();
        InitializeComponent();

        // The view model subscribes to the singleton monitoring service; release
        // it here or repeated navigation would accumulate subscribers.
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    /// <summary>View model backing this page.</summary>
    public DiagnosticsViewModel ViewModel { get; }
}
