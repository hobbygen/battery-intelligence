using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>Application configuration. Specification section 35.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        // Frame navigation constructs pages parameterlessly, so the view model is
        // resolved here. This is the single service-location point in the
        // application, confined to page constructors by convention.
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    /// <summary>View model backing this page.</summary>
    public SettingsViewModel ViewModel { get; }
}
