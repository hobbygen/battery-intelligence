using BatteryIntelligence.Core.Configuration;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Loads, exposes and persists user settings.
/// </summary>
/// <remarks>
/// Declared in Core and implemented in the Data layer, so that consumers depend
/// on the contract rather than on the JSON file behind it.
/// </remarks>
public interface ISettingsService
{
    /// <summary>
    /// The current settings. Never <see langword="null"/>: before a successful
    /// load, and after a failed one, this returns validated defaults.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>Raised after settings change, on an unspecified thread.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Loads settings from disk. A missing, unreadable or malformed file yields
    /// defaults rather than an exception.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a mutation, validates the result, persists it and raises
    /// <see cref="Changed"/>.
    /// </summary>
    /// <param name="mutate">Action applied to the settings instance.</param>
    /// <param name="category">
    /// Optional category name carried on the change notification, letting
    /// listeners ignore changes they do not care about.
    /// </param>
    /// <param name="cancellationToken">Cancels the persist operation.</param>
    Task UpdateAsync(
        Action<AppSettings> mutate,
        string? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>Writes current settings to disk without mutating them.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Describes a settings change.</summary>
/// <param name="Settings">The settings after the change.</param>
/// <param name="Category">The category affected, when the caller supplied one.</param>
public sealed record SettingsChangedEventArgs(AppSettings Settings, string? Category);
