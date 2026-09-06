using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.App.Services;

/// <summary>
/// Forwards every battery reading from <see cref="IBatteryMonitoringService"/> to
/// <see cref="IBatterySampleWriteQueue"/>, tagged with the current session and
/// screen state, and flushes the queue immediately on suspend.
/// </summary>
/// <remarks>
/// This is the composition-root glue docs/architecture.md section 2 calls
/// "monitoring orchestration": Battery, Sessions and Data are siblings that must
/// not reference each other, so the only place they can be connected is here, in
/// App. The class does nothing but wire events to methods — any real logic
/// belongs in one of the sides it connects, not in the glue between them.
/// </remarks>
public sealed class BatteryPersistenceBridge : IHostedService
{
    private readonly IBatteryMonitoringService _monitoring;
    private readonly IBatterySampleWriteQueue _writeQueue;
    private readonly ISessionMonitoringService _sessions;
    private readonly BatteryMessageWindow? _messageWindow;
    private readonly ILogger<BatteryPersistenceBridge> _logger;

    public BatteryPersistenceBridge(
        IBatteryMonitoringService monitoring,
        IBatterySampleWriteQueue writeQueue,
        ISessionMonitoringService sessions,
        ILogger<BatteryPersistenceBridge> logger,
        BatteryMessageWindow? messageWindow = null)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(writeQueue);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(logger);

        _monitoring = monitoring;
        _writeQueue = writeQueue;
        _sessions = sessions;
        _logger = logger;
        _messageWindow = messageWindow;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _monitoring.Updated += OnMonitoringUpdated;

        if (_messageWindow is not null)
        {
            _messageWindow.Suspended += OnSuspended;
        }

        // IHost starts hosted services sequentially in registration order,
        // fully awaiting each StartAsync before the next begins. Because this
        // bridge is registered after BatteryMonitoringService, that service's
        // own startup refresh — and its Updated event — can complete before the
        // subscription above exists. Enqueuing whatever is already cached here
        // closes that race without depending on registration order.
        OnMonitoringUpdated(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _monitoring.Updated -= OnMonitoringUpdated;

        if (_messageWindow is not null)
        {
            _messageWindow.Suspended -= OnSuspended;
        }

        return Task.CompletedTask;
    }

    private void OnSuspended(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        // docs/session-engine.md section 5, "on suspend": nothing may be lost to
        // the suspend, so the queue is flushed immediately rather than waiting
        // for its normal 30-second/200-row triggers.
        _ = _writeQueue.FlushAsync().ContinueWith(
            t => _logger.LogWarning(t.Exception, "Suspend flush failed."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void OnMonitoringUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        foreach (BatterySnapshot snapshot in _monitoring.CurrentSnapshots)
        {
            long? sessionId = _sessions.GetOpenSessionId(snapshot.Device.HardwareId);
            _writeQueue.Enqueue(snapshot, _sessions.CurrentScreenState, sessionId);
        }
    }
}
