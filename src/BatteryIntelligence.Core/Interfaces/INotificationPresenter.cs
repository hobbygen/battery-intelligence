using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Delivers an alert to the operating system's notification surface — a Windows
/// toast (specification section 21). Declared in Core, implemented in App
/// (the only place the WinAppSDK notification API is touched).
/// </summary>
/// <remarks>
/// The in-app alert centre is the guaranteed floor: the alert engine persists and
/// raises every alert regardless of this presenter. A presenter that cannot
/// deliver returns <see langword="false"/> (or reports
/// <see cref="IsAvailable"/> = <see langword="false"/>) rather than throwing —
/// spec §21 requires a notification failure to degrade to in-app without error.
/// </remarks>
public interface INotificationPresenter
{
    /// <summary>Whether OS notifications can currently be shown at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Shows one alert as an OS notification. Returns <see langword="false"/> if it could not be delivered.</summary>
    Task<bool> ShowAsync(Alert alert, bool playSound, CancellationToken cancellationToken = default);
}
