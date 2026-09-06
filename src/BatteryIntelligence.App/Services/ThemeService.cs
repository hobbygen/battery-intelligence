using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BatteryIntelligence.App.Services;

/// <summary>Applies the user's theme choice and window backdrop.</summary>
public interface IThemeService
{
    /// <summary>The theme currently applied.</summary>
    ThemePreference Current { get; }

    /// <summary>Registers the window whose content theme is controlled.</summary>
    void Initialize(Window window);

    /// <summary>Applies a theme immediately, without restarting the application.</summary>
    void Apply(ThemePreference theme);
}

/// <summary>
/// Default <see cref="IThemeService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The theme is applied to the window's root <see cref="FrameworkElement"/>
/// rather than to <see cref="Application.RequestedTheme"/>, because the latter can
/// only be set once at startup and would require a restart to change — which
/// specification section 35 does not permit.
/// </para>
/// <para>
/// Mica is applied only where supported. On Windows 10 the backdrop is left
/// unset, which yields the standard solid window background rather than a broken
/// or transparent one.
/// </para>
/// </remarks>
public sealed class ThemeService : IThemeService
{
    private readonly ILogger<ThemeService> _logger;
    private readonly ISettingsService _settings;
    private Window? _window;

    public ThemeService(ILogger<ThemeService> logger, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);

        _logger = logger;
        _settings = settings;
    }

    /// <inheritdoc/>
    public ThemePreference Current { get; private set; } = ThemePreference.System;

    /// <inheritdoc/>
    public void Initialize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;
        ApplyBackdrop(window);
        Apply(_settings.Current.Appearance.Theme);
    }

    /// <inheritdoc/>
    public void Apply(ThemePreference theme)
    {
        Current = theme;

        if (_window?.Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = theme switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        _logger.LogDebug("Applied theme {Theme}.", theme);
    }

    private void ApplyBackdrop(Window window)
    {
        try
        {
            if (MicaController.IsSupported())
            {
                window.SystemBackdrop = new MicaBackdrop();
                _logger.LogDebug("Mica backdrop enabled.");
            }
            else
            {
                // Windows 10, or a machine without composition support. A solid
                // background is the correct fallback, not an error.
                _logger.LogInformation("Mica unavailable; using the default window background.");
            }
        }
        catch (Exception ex)
        {
            // A backdrop is cosmetic. Losing it must never prevent the window
            // from appearing.
            _logger.LogWarning(ex, "Could not apply the window backdrop.");
        }
    }
}
