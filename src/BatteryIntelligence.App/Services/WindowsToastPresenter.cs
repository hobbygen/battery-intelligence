using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BatteryIntelligence.App.Services;

/// <summary>
/// Delivers alerts as Windows toast notifications via the Windows App SDK
/// (specification section 21). The only file in the solution that touches the
/// notification API.
/// </summary>
/// <remarks>
/// This is an <em>unpackaged</em> app, so <c>AppNotificationManager.Register()</c>
/// creates the Start-menu shortcut and COM activator the platform needs. Every
/// call is wrapped: if registration or a <c>Show</c> fails — a locked-down box, a
/// group policy, a broken shell — <see cref="IsAvailable"/> stays
/// <see langword="false"/> and <see cref="ShowAsync"/> returns
/// <see langword="false"/>, and the alert engine's in-app centre carries the alert
/// on its own (spec §21: a notification failure degrades to in-app without error).
/// </remarks>
public sealed class WindowsToastPresenter : INotificationPresenter
{
    private readonly ILogger<WindowsToastPresenter> _logger;

    public WindowsToastPresenter(ILogger<WindowsToastPresenter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsAvailable { get; private set; }

    /// <summary>Registers the app with the OS notification platform. Called once at startup.</summary>
    public void Register()
    {
        try
        {
            AppNotificationManager.Default.Register();
            IsAvailable = true;
            _logger.LogInformation("Windows notifications registered.");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            _logger.LogWarning(ex, "Windows notifications unavailable; alerts will be delivered in-app only.");
        }
    }

    /// <summary>Unregisters at shutdown. Best-effort.</summary>
    public void Unregister()
    {
        if (!IsAvailable)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Windows notification unregister failed.");
        }
    }

    /// <inheritdoc/>
    public Task<bool> ShowAsync(Alert alert, bool playSound, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (!IsAvailable)
        {
            return Task.FromResult(false);
        }

        try
        {
            AppNotificationBuilder builder = new AppNotificationBuilder()
                .AddText(alert.Title)
                .AddText(alert.Message);

            if (alert.Severity == AlertSeverity.Critical)
            {
                builder.SetScenario(AppNotificationScenario.Urgent);
            }

            if (!playSound)
            {
                builder.MuteAudio();
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to show a toast for {Type}.", alert.Type);
            return Task.FromResult(false);
        }
    }
}
