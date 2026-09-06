using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Sessions;
using BatteryIntelligence.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Sessions;

/// <summary>
/// Drives one <see cref="SessionStateMachine"/> per battery from real
/// <see cref="IBatteryMonitoringService"/> readings and Windows notifications,
/// and persists the result through <see cref="ISessionStore"/>
/// (docs/session-engine.md; docs/architecture.md section 5).
/// </summary>
/// <remarks>
/// The state machine itself is pure (Core.Sessions); everything hardware- or
/// database-shaped lives here instead, which is exactly the split
/// docs/session-engine.md section 8 asks for.
/// </remarks>
public sealed class SessionMonitoringService : ISessionMonitoringService, IHostedService, IDisposable
{
    /// <summary>
    /// How recent the last sample must be, at startup, for an open session found
    /// in the database to be adopted rather than closed as interrupted
    /// (docs/session-engine.md section 6).
    /// </summary>
    private static readonly TimeSpan CrashRecoveryGrace = TimeSpan.FromMinutes(5);

    private readonly IBatteryMonitoringService _batteryMonitoring;
    private readonly ISessionStore _store;
    private readonly BatteryMessageWindow? _messageWindow;
    private readonly ILogger<SessionMonitoringService> _logger;
    private readonly IMonitoringStatusRegistry _status;
    private readonly Dictionary<string, BatteryContext> _contexts = [];
    private readonly SemaphoreSlim _tickGate = new(1, 1);

    private volatile bool _started;
    private ScreenState _screenState = ScreenState.Unknown;
    private LockState _lockState = LockState.Unknown;
    private DateTimeOffset? _suspendedAtUtc;

    public SessionMonitoringService(
        IBatteryMonitoringService batteryMonitoring,
        ISessionStore store,
        ILogger<SessionMonitoringService> logger,
        IMonitoringStatusRegistry status,
        BatteryMessageWindow? messageWindow = null)
    {
        ArgumentNullException.ThrowIfNull(batteryMonitoring);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _batteryMonitoring = batteryMonitoring;
        _store = store;
        _logger = logger;
        _status = status;
        _messageWindow = messageWindow;
    }

    public BatterySessionInfo? CurrentSession { get; private set; }

    public ScreenState CurrentScreenState => _screenState;

    public LockState CurrentLockState => _lockState;

    public event EventHandler? Updated;

    public long? GetOpenSessionId(string batteryId) =>
        _contexts.TryGetValue(batteryId, out BatteryContext? context) ? context.Machine.CurrentSession?.Id : null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_messageWindow is not null)
        {
            _messageWindow.Suspended += OnSuspended;
            _messageWindow.Resumed += OnResumed;
            _messageWindow.ScreenStateChanged += OnScreenStateChanged;
            _messageWindow.LockStateChanged += OnLockStateChanged;
        }

        _batteryMonitoring.Updated += OnBatteryUpdated;
        _started = true;

        // Mirrors the Phase 3 fix in BatteryPersistenceBridge: hosted services
        // start sequentially, so whatever the battery monitor already read
        // before this service subscribed must still be picked up.
        _ = OnBatteryUpdatedAsync();

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;

        _batteryMonitoring.Updated -= OnBatteryUpdated;
        if (_messageWindow is not null)
        {
            _messageWindow.Suspended -= OnSuspended;
            _messageWindow.Resumed -= OnResumed;
            _messageWindow.ScreenStateChanged -= OnScreenStateChanged;
            _messageWindow.LockStateChanged -= OnLockStateChanged;
        }

        // Persist final accumulators so a clean shutdown loses nothing, even
        // though each tick already writes them (belt and braces given how cheap
        // this is at session-scale write volume).
        foreach (BatteryContext context in _contexts.Values)
        {
            if (context.Machine.CurrentSession is { Id: long id } info)
            {
                try
                {
                    await _store.UpdateOpenSessionAsync(id, info, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not persist final session state for session {Id} on shutdown.", id);
                }
            }
        }
    }

    public Task<IReadOnlyList<BatterySessionInfo>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken = default) =>
        _store.GetRecentSessionsAsync(count, cancellationToken);

    public Task<IReadOnlyList<TimelineSegment>> GetTimelineAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
        _store.GetTimelineAsync(fromUtc, toUtc, cancellationToken);

    private void OnSuspended(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _suspendedAtUtc = now;

        foreach (BatteryContext context in _contexts.Values)
        {
            context.Machine.ProcessSuspend(now);
        }

        _ = SafeRecordSystemEventAsync(SystemEventKind.Suspend, now, inferred: false, null);
    }

    private void OnResumed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset suspendedAt = _suspendedAtUtc ?? now;

        foreach (BatteryContext context in _contexts.Values)
        {
            context.Machine.ProcessResume(now, suspendedAt);
        }

        _suspendedAtUtc = null;
        _ = SafeRecordSystemEventAsync(SystemEventKind.Resume, now, inferred: false, null);
        _ = PersistOpenSessionsAsync();
    }

    private void OnScreenStateChanged(object? sender, ScreenState state)
    {
        _ = sender;
        _screenState = state;

        SystemEventKind kind = state switch
        {
            ScreenState.On => SystemEventKind.ScreenOn,
            ScreenState.Off => SystemEventKind.ScreenOff,
            ScreenState.Dimmed => SystemEventKind.ScreenDimmed,
            _ => SystemEventKind.Unknown,
        };

        if (kind != SystemEventKind.Unknown)
        {
            _ = SafeRecordSystemEventAsync(kind, DateTimeOffset.UtcNow, inferred: false, null);
        }
    }

    private void OnLockStateChanged(object? sender, LockState state)
    {
        _ = sender;
        _lockState = state;

        SystemEventKind kind = state == LockState.Locked ? SystemEventKind.Locked : SystemEventKind.Unlocked;
        _ = SafeRecordSystemEventAsync(kind, DateTimeOffset.UtcNow, inferred: false, null);
    }

    private void OnBatteryUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _ = OnBatteryUpdatedAsync();
    }

    private async Task OnBatteryUpdatedAsync()
    {
        if (!_started || !await _tickGate.WaitAsync(0).ConfigureAwait(false))
        {
            // Not started yet, or a previous tick (or the resume handler) is
            // still in flight — the next Updated event will catch up.
            return;
        }

        string? tickError = null;
        try
        {
            foreach (BatterySnapshot snapshot in _batteryMonitoring.CurrentSnapshots)
            {
                if (snapshot.Device.IsAggregate)
                {
                    continue;
                }

                try
                {
                    await ProcessSnapshotAsync(snapshot).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // One battery's session tracking failing must not stop the
                    // others (specification section 44).
                    tickError = ex.Message;
                    _logger.LogWarning(ex, "Session tracking failed for battery {BatteryId}.", snapshot.Device.HardwareId);
                }
            }
        }
        finally
        {
            _tickGate.Release();
        }

        if (tickError is null)
        {
            _status.ReportSuccess(MonitoringComponent.Sessions);
        }
        else
        {
            _status.ReportFailure(MonitoringComponent.Sessions, tickError);
        }

        CurrentSession = _contexts.Values.FirstOrDefault()?.Machine.CurrentSession;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private async Task ProcessSnapshotAsync(BatterySnapshot snapshot)
    {
        string batteryId = snapshot.Device.HardwareId;

        if (!_contexts.TryGetValue(batteryId, out BatteryContext? context))
        {
            long deviceId = await _store.GetOrCreateDeviceIdAsync(snapshot.Device, snapshot.Info.TimestampUtc).ConfigureAwait(false);
            SessionStateMachine machine = new();
            context = new BatteryContext(deviceId, machine);
            _contexts[batteryId] = context;

            await RecoverAsync(deviceId, machine).ConfigureAwait(false);
        }

        SessionEngineInput input = new(
            snapshot.Info.TimestampUtc,
            Environment.TickCount64,
            batteryId,
            snapshot.Info.State.Value ?? BatteryState.Unknown,
            snapshot.Info.AcOnline.Value,
            snapshot.Info.Percentage.Value,
            snapshot.Info.RemainingCapacityMWh.Value,
            _screenState,
            _lockState);

        SessionEngineTickResult result = context.Machine.Process(input);
        await ApplyTickResultAsync(context, result).ConfigureAwait(false);
    }

    private async Task RecoverAsync(long deviceId, SessionStateMachine machine)
    {
        try
        {
            BatterySessionInfo? open = await _store.GetOpenSessionAsync(deviceId).ConfigureAwait(false);
            if (open is null)
            {
                return;
            }

            LastBatterySample? last = await _store.GetLastSampleAsync(deviceId).ConfigureAwait(false);
            bool recent = last is not null && DateTimeOffset.UtcNow - last.TimestampUtc <= CrashRecoveryGrace;

            if (recent)
            {
                machine.Adopt(open);
                await _store.RecordSessionEventAsync(
                    open.Id!.Value, SessionEventKind.Adopted, DateTimeOffset.UtcNow, null,
                    "Session continued after an application restart.", inferred: false).ConfigureAwait(false);
                _logger.LogInformation("Adopted open session {Id} after restart.", open.Id);
            }
            else
            {
                DateTimeOffset endUtc = last?.TimestampUtc ?? open.StartUtc;
                BatterySessionInfo closed = open with
                {
                    EndUtc = endUtc,
                    EndPercentage = last?.Percentage ?? open.StartPercentage,
                    ClosedCleanly = false,
                    EndReason = SessionEndReason.Interrupted,
                };

                await _store.CloseSessionAsync(open.Id!.Value, closed).ConfigureAwait(false);
                _logger.LogInformation(
                    "Closed session {Id} as Interrupted — last sample predates the {Grace} adoption grace window.",
                    open.Id, CrashRecoveryGrace);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session recovery failed for device {DeviceId}.", deviceId);
        }
    }

    private async Task ApplyTickResultAsync(BatteryContext context, SessionEngineTickResult result)
    {
        if (result.Rejected)
        {
            return;
        }

        if (result.ClosedSession is { Id: long closedId } closed)
        {
            await _store.CloseSessionAsync(closedId, closed).ConfigureAwait(false);
        }

        if (result.OpenedSession is BatterySessionInfo opened)
        {
            long id = await _store.InsertOpenSessionAsync(opened, context.DeviceId).ConfigureAwait(false);
            context.Machine.AssignSessionId(id);
        }

        if (result.InterruptionRecorded && context.Machine.CurrentSession is { Id: long interruptedSessionId } current)
        {
            await _store.RecordSessionEventAsync(
                interruptedSessionId, SessionEventKind.Interruption, DateTimeOffset.UtcNow, current.EndPercentage,
                "Charging paused and resumed before the interruption grace period elapsed.", inferred: false).ConfigureAwait(false);
        }

        if (result.InferredSystemEvent is SystemEventKind inferredKind)
        {
            await SafeRecordSystemEventAsync(
                inferredKind, DateTimeOffset.UtcNow, inferred: true,
                "Inferred from a wall-clock/uptime gap between consecutive samples.").ConfigureAwait(false);
        }

        if (result.PercentageJumpSuspect && context.Machine.CurrentSession is { Id: long jumpSessionId })
        {
            await _store.RecordSessionEventAsync(
                jumpSessionId, SessionEventKind.PercentageJumpSuspect, DateTimeOffset.UtcNow, null,
                "Percentage changed implausibly while the system was awake.", inferred: false).ConfigureAwait(false);
        }

        // Periodic persistence of running totals for a session that stayed open
        // this tick (a session that just opened or closed already has its
        // correct row from the branches above).
        if (result.ClosedSession is null && result.OpenedSession is null && result.OpenSessionState is { Id: long updateId } stillOpen)
        {
            await _store.UpdateOpenSessionAsync(updateId, stillOpen).ConfigureAwait(false);
        }
    }

    private async Task PersistOpenSessionsAsync()
    {
        foreach (BatteryContext context in _contexts.Values)
        {
            if (context.Machine.CurrentSession is { Id: long id } info)
            {
                await _store.UpdateOpenSessionAsync(id, info).ConfigureAwait(false);
            }
        }
    }

    private async Task SafeRecordSystemEventAsync(SystemEventKind kind, DateTimeOffset timestampUtc, bool inferred, string? detail)
    {
        try
        {
            await _store.RecordSystemEventAsync(kind, timestampUtc, inferred, detail).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record system event {Kind}.", kind);
        }
    }

    public void Dispose() => _tickGate.Dispose();

    private sealed record BatteryContext(long DeviceId, SessionStateMachine Machine);
}
