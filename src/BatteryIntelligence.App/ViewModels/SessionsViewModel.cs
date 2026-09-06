using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>
/// Backs the Sessions page: current session detail and the historical session
/// list (specification section 12; docs/ui-navigation.md).
/// </summary>
/// <remarks>
/// Subscribes to the singleton <see cref="ISessionMonitoringService"/> for the
/// life of the page — see <see cref="BatteryViewModel"/>'s remarks for why this
/// is <see cref="IDisposable"/>.
/// </remarks>
public sealed partial class SessionsViewModel : ObservableObject, IDisposable
{
    private const int RecentSessionCount = 20;

    private readonly ISessionMonitoringService _sessions;
    private readonly ILogger<SessionsViewModel> _logger;
    private readonly DispatcherQueue _dispatcher;

    private SessionDisplay? _current;
    private IReadOnlyList<SessionDisplay> _recent = [];
    private bool _isLoading;

    public SessionsViewModel(ISessionMonitoringService sessions, ILogger<SessionsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(logger);

        _sessions = sessions;
        _logger = logger;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _sessions.Updated += OnSessionsUpdated;
        _ = RefreshAsync();
    }

    /// <summary>The in-progress session, or <see langword="null"/> when idle.</summary>
    public SessionDisplay? Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    /// <summary>Whether a session is currently open — drives the empty state.</summary>
    public bool HasCurrentSession => Current is not null;

    public bool NoCurrentSession => !HasCurrentSession;

    /// <summary>Most recent sessions, newest first.</summary>
    public IReadOnlyList<SessionDisplay> Recent
    {
        get => _recent;
        private set => SetProperty(ref _recent, value);
    }

    public bool HasRecentSessions => Recent.Count > 0;

    public bool NoRecentSessions => !HasRecentSessions;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            BatterySessionInfo? current = _sessions.CurrentSession;

            IReadOnlyList<BatterySessionInfo> recent = [];
            try
            {
                recent = await _sessions.GetRecentSessionsAsync(RecentSessionCount).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load recent sessions.");
            }

            Current = current is null ? null : SessionDisplay.From(current, now);
            Recent = [.. recent
                .Where(s => current is null || s.Id != current.Id)
                .Select(s => SessionDisplay.From(s, now))];

            OnPropertyChanged(nameof(HasCurrentSession));
            OnPropertyChanged(nameof(NoCurrentSession));
            OnPropertyChanged(nameof(HasRecentSessions));
            OnPropertyChanged(nameof(NoRecentSessions));
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnSessionsUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(() => _ = RefreshAsync());
    }

    public void Dispose() => _sessions.Updated -= OnSessionsUpdated;
}
