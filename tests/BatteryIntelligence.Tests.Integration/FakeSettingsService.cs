using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Interfaces;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>A minimal <see cref="ISettingsService"/> for tests that only need <see cref="Current"/>.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public FakeSettingsService(AppSettings? settings = null)
    {
        Current = settings ?? new AppSettings();
    }

    public AppSettings Current { get; }

    public event EventHandler<SettingsChangedEventArgs>? Changed { add { } remove { } }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateAsync(Action<AppSettings> mutate, string? category = null, CancellationToken cancellationToken = default)
    {
        mutate(Current);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
