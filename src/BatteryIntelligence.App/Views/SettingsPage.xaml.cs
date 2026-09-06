using BatteryIntelligence.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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

    private async void OnDeleteAllHistoryClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        var confirmBox = new TextBox
        {
            PlaceholderText = SettingsViewModel.DeleteConfirmationWord,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete all battery history?",
            PrimaryButtonText = "Delete everything",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = $"Every sample, session, health snapshot, insight and alert will be permanently removed. Your battery device and settings are kept.\n\nType {SettingsViewModel.DeleteConfirmationWord} to confirm.",
                    },
                    confirmBox,
                },
            },
        };

        confirmBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled =
                string.Equals(confirmBox.Text.Trim(), SettingsViewModel.DeleteConfirmationWord, StringComparison.Ordinal);

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        string message = await ViewModel.DeleteAllHistoryAsync();
        DeleteResultBar.Message = message;
        DeleteResultBar.IsOpen = true;
    }
}
