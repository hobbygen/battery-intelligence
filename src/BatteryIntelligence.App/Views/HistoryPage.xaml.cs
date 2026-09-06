using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>History. Tier-aware charts over 24 hours to a year, plus CSV / JSON export.</summary>
public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        InitializeComponent();

        Unloaded += (_, _) => ViewModel.Dispose();
    }

    public HistoryViewModel ViewModel { get; }
}
