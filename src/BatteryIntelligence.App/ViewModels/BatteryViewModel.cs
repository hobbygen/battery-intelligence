using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>
/// Backs the Battery page and the Dashboard's battery card: identity, capacity,
/// health and live state for every present battery (specification section 8-9).
/// </summary>
/// <remarks>
/// Subscribes to the singleton <see cref="IBatteryMonitoringService"/> for the
/// life of the page. Because <see cref="Services.NavigationService"/> creates a
/// new page (and therefore a new transient ViewModel) on every navigation, this
/// type is <see cref="IDisposable"/> and must be disposed when its page unloads,
/// or the subscription would accumulate across repeated navigation.
/// </remarks>
public sealed partial class BatteryViewModel : ObservableObject, IDisposable
{
    private readonly IBatteryMonitoringService _monitoring;
    private readonly DispatcherQueue _dispatcher;

    private bool _hasBattery;
    private string? _lastError;
    private IReadOnlyList<BatteryCardDisplay> _batteries = [];
    private BatteryCardDisplay? _aggregate;

    public BatteryViewModel(IBatteryMonitoringService monitoring)
    {
        ArgumentNullException.ThrowIfNull(monitoring);

        _monitoring = monitoring;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _monitoring.Updated += OnMonitoringUpdated;
        ApplySnapshot();
    }

    /// <summary>Whether at least one battery is present. Drives the empty state.</summary>
    public bool HasBattery
    {
        get => _hasBattery;
        private set
        {
            if (SetProperty(ref _hasBattery, value))
            {
                OnPropertyChanged(nameof(NoBattery));
            }
        }
    }

    /// <summary>Inverse of <see cref="HasBattery"/>, for binding the empty state's visibility directly.</summary>
    public bool NoBattery => !HasBattery;

    /// <summary>The most recent monitoring failure, or <see langword="null"/> when the last read succeeded.</summary>
    public string? LastError
    {
        get => _lastError;
        private set
        {
            if (SetProperty(ref _lastError, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>Whether <see cref="LastError"/> is set, for binding a warning banner's visibility directly.</summary>
    public bool HasError => _lastError is not null;

    /// <summary>One display card per present battery device.</summary>
    public IReadOnlyList<BatteryCardDisplay> Batteries
    {
        get => _batteries;
        private set => SetProperty(ref _batteries, value);
    }

    /// <summary>Whether more than one battery is present, so the aggregate card is worth showing.</summary>
    public bool HasMultipleBatteries => Batteries.Count > 1;

    /// <summary>The system-wide aggregate, or <see langword="null"/> when zero or one battery is present.</summary>
    public BatteryCardDisplay? Aggregate
    {
        get => _aggregate;
        private set => SetProperty(ref _aggregate, value);
    }

    /// <summary>
    /// The single card the Dashboard's condensed battery summary shows: the
    /// aggregate when multiple batteries are present, otherwise the one battery.
    /// </summary>
    public BatteryCardDisplay? Primary => Aggregate ?? Batteries.FirstOrDefault();

    [RelayCommand]
    private async Task RefreshAsync() => await _monitoring.RefreshAsync().ConfigureAwait(false);

    private void OnMonitoringUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        // Updated fires on the thread pool (IBatteryMonitoringService contract);
        // every bound property must change on the UI thread.
        _dispatcher.TryEnqueue(ApplySnapshot);
    }

    private void ApplySnapshot()
    {
        IReadOnlyList<BatterySnapshot> snapshots = _monitoring.CurrentSnapshots;

        HasBattery = snapshots.Count > 0;
        LastError = _monitoring.LastError;
        Batteries = [.. snapshots.Select(BatteryCardDisplay.From)];
        Aggregate = snapshots.Count > 1 && _monitoring.Aggregate is BatteryInfo aggregate
            ? BatteryCardDisplay.From(new BatterySnapshot(
                new Core.Models.BatteryDevice(
                    Core.Models.BatteryDevice.AggregateHardwareId, null, null, null, null,
                    aggregate.DesignCapacityMWh, Core.Primitives.Measurement<int>.Unavailable(), false),
                aggregate))
            : null;

        OnPropertyChanged(nameof(HasMultipleBatteries));
        OnPropertyChanged(nameof(Primary));
    }

    public void Dispose() => _monitoring.Updated -= OnMonitoringUpdated;
}
