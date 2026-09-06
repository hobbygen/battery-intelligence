using System.Globalization;
using BatteryIntelligence.App.Services;
using BatteryIntelligence.Core.Constants;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>
/// Backs the Settings page.
/// </summary>
/// <remarks>
/// Only the categories with something to configure in this phase are surfaced.
/// Monitoring, alert, notification and data settings exist in the settings model
/// already, but are not shown until the subsystems they control are built —
/// exposing controls that change nothing would be worse than omitting them.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IHistoryMaintenance _historyMaintenance;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        IHistoryMaintenance historyMaintenance,
        ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(historyMaintenance);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _theme = theme;
        _historyMaintenance = historyMaintenance;
        _logger = logger;
    }

    /// <summary>The exact word the user must type to confirm deleting all history.</summary>
    public static string DeleteConfirmationWord => "DELETE";

    /// <summary>Theme options, in the order shown in the combo box.</summary>
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    /// <summary>Density options, in the order shown in the combo box.</summary>
    public IReadOnlyList<string> DensityOptions { get; } = ["Comfortable", "Compact"];

    /// <summary>Selected index into <see cref="ThemeOptions"/>.</summary>
    public int SelectedThemeIndex
    {
        get => (int)_settings.Current.Appearance.Theme;
        set
        {
            if (value < 0 || value == SelectedThemeIndex)
            {
                return;
            }

            ThemePreference theme = (ThemePreference)value;

            // Apply before persisting, so the change is visible immediately
            // rather than after a disk write completes.
            _theme.Apply(theme);
            _ = _settings.UpdateAsync(s => s.Appearance.Theme = theme, "Appearance");
            OnPropertyChanged();
        }
    }

    /// <summary>Selected index into <see cref="DensityOptions"/>.</summary>
    public int SelectedDensityIndex
    {
        get => (int)_settings.Current.Appearance.Density;
        set
        {
            if (value < 0 || value == SelectedDensityIndex)
            {
                return;
            }

            DisplayDensity density = (DisplayDensity)value;
            _ = _settings.UpdateAsync(s => s.Appearance.Density = density, "Appearance");
            OnPropertyChanged();
        }
    }

    /// <summary>Whether closing the window keeps monitoring running in the tray.</summary>
    public bool MinimizeToTray
    {
        get => _settings.Current.General.MinimizeToTray;
        set
        {
            if (value == MinimizeToTray)
            {
                return;
            }

            _ = _settings.UpdateAsync(s => s.General.MinimizeToTray = value, "General");
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartMinimized));
        }
    }

    /// <summary>Whether the application starts without showing its window.</summary>
    public bool StartMinimized
    {
        get => _settings.Current.General.StartMinimized;
        set
        {
            if (value == StartMinimized)
            {
                return;
            }

            _ = _settings.UpdateAsync(s => s.General.StartMinimized = value, "General");
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Starting minimized is only meaningful when there is a tray icon to
    /// minimize into.
    /// </summary>
    public bool CanStartMinimized => MinimizeToTray;

    /// <summary>Where settings, logs and the database live.</summary>
    /// <remarks>An instance property so that it is bindable with x:Bind.</remarks>
    public string DataDirectory => AppPaths.DataDirectory;

    /// <summary>Full path of the settings file, shown for transparency.</summary>
    public string SettingsFilePath => AppPaths.SettingsFile;

    /// <summary>
    /// Deletes every telemetry, session, health, insight and alert row and
    /// reclaims the space (specification section 29). Identity and configuration
    /// rows are kept. Returns a short result message for the page to show.
    /// </summary>
    public async Task<string> DeleteAllHistoryAsync()
    {
        try
        {
            await _historyMaintenance.DeleteAllAsync().ConfigureAwait(true);
            _logger.LogInformation("All battery history deleted at the user's request.");
            return "All battery history has been deleted.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting battery history failed.");
            return string.Create(CultureInfo.CurrentCulture, $"Could not delete history: {ex.Message}");
        }
    }
}
