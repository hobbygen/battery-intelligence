using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace BatteryIntelligence.App.Services;

/// <summary>Restores and persists main-window geometry.</summary>
public interface IWindowStateService
{
    /// <summary>Applies the saved geometry to a window, if it is still valid.</summary>
    void Restore(Window window);

    /// <summary>Captures and persists the window's current geometry.</summary>
    Task PersistAsync(Window window);
}

/// <summary>
/// Default <see cref="IWindowStateService"/>.
/// </summary>
/// <remarks>
/// Restored geometry is validated against the displays that are actually attached
/// now. A window saved on a second monitor that has since been unplugged would
/// otherwise restore off-screen, leaving the application running but
/// unreachable — a failure that is both easy to cause and hard for a user to
/// diagnose.
/// </remarks>
public sealed class WindowStateService : IWindowStateService
{
    private readonly ILogger<WindowStateService> _logger;
    private readonly ISettingsService _settings;

    public WindowStateService(ILogger<WindowStateService> logger, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);

        _logger = logger;
        _settings = settings;
    }

    /// <inheritdoc/>
    public void Restore(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        WindowStateSettings state = _settings.Current.WindowState;
        AppWindow appWindow = window.AppWindow;

        int width = Math.Max(state.Width, WindowStateSettings.MinimumWidth);
        int height = Math.Max(state.Height, WindowStateSettings.MinimumHeight);

        if (state.Left < 0 || state.Top < 0)
        {
            // First run: size only, and let Windows place the window.
            appWindow.Resize(new SizeInt32(width, height));
            return;
        }

        RectInt32 desired = new(state.Left, state.Top, width, height);

        if (!IsSufficientlyVisible(desired))
        {
            _logger.LogInformation(
                "Saved window position {Left},{Top} is not on any attached display; centring instead.",
                state.Left,
                state.Top);

            appWindow.Resize(new SizeInt32(width, height));
            return;
        }

        appWindow.MoveAndResize(desired);

        if (state.IsMaximized && appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    /// <inheritdoc/>
    public async Task PersistAsync(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            AppWindow appWindow = window.AppWindow;
            bool maximized = appWindow.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Maximized,
            };

            await _settings.UpdateAsync(
                settings =>
                {
                    // A maximized window's restore bounds are not meaningful to
                    // capture here, so keep the previous size and record only the
                    // maximized flag.
                    if (!maximized)
                    {
                        settings.WindowState.Left = appWindow.Position.X;
                        settings.WindowState.Top = appWindow.Position.Y;
                        settings.WindowState.Width = appWindow.Size.Width;
                        settings.WindowState.Height = appWindow.Size.Height;
                    }

                    settings.WindowState.IsMaximized = maximized;
                },
                category: nameof(WindowStateSettings)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Losing window geometry is a cosmetic regression, never a reason to
            // interfere with shutdown.
            _logger.LogWarning(ex, "Could not persist window state.");
        }
    }

    /// <summary>
    /// Whether enough of the requested rectangle overlaps an attached display for
    /// the window to be usable.
    /// </summary>
    private static bool IsSufficientlyVisible(RectInt32 desired)
    {
        // Probe the title-bar region rather than the whole rectangle: a window is
        // recoverable as long as the user can grab its title bar.
        PointInt32 titleBarPoint = new(desired.X + (desired.Width / 2), desired.Y + 16);

        DisplayArea? area = DisplayArea.GetFromPoint(titleBarPoint, DisplayAreaFallback.None);
        return area is not null;
    }
}
