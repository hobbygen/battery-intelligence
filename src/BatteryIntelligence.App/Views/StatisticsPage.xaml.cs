using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BatteryIntelligence.App.Views;

/// <summary>Statistics. Today / 7-day / 30-day / lifetime aggregate usage.</summary>
public sealed partial class StatisticsPage : Page
{
    public StatisticsPage()
    {
        ViewModel = App.Services.GetRequiredService<StatisticsViewModel>();
        InitializeComponent();

        Unloaded += (_, _) => ViewModel.Dispose();
    }

    public StatisticsViewModel ViewModel { get; }
}
