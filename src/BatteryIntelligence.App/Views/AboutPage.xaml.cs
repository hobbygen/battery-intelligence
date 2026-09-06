using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>About. Application identity, version and system facts.</summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        ViewModel = App.Services.GetRequiredService<AboutViewModel>();
        InitializeComponent();
    }

    /// <summary>Backs the About page.</summary>
    public AboutViewModel ViewModel { get; }
}
