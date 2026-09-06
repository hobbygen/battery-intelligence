using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Thermal;

/// <summary>
/// Turns a stream of temperature readings into <see cref="ThresholdEvent"/>s: a
/// new event is confirmed only once the reading has stayed at or above the
/// warning threshold continuously for the dwell time (default 60 s), so a brief
/// spike never creates one. Pure and clock-injected — unit-tested without waiting.
/// </summary>
/// <remarks>
/// Not thread-safe; the owning <c>ThermalMonitoringService</c> serialises access.
/// One instance per battery.
/// </remarks>
public sealed class ThresholdEventDetector
{
    private readonly TimeSpan _dwell;

    private DateTimeOffset? _aboveSinceUtc;
    private bool _confirmed;
    private double _peakCelsius;
    private PowerDirection _peakContext;

    /// <param name="dwell">How long the reading must hold above the threshold before an event is confirmed.</param>
    public ThresholdEventDetector(TimeSpan? dwell = null)
    {
        _dwell = dwell ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>The event currently in progress, or null.</summary>
    public ThresholdEvent? OpenEvent { get; private set; }

    /// <summary>
    /// Feeds one reading. Returns a <em>closed</em> <see cref="ThresholdEvent"/> at
    /// the moment the temperature drops back below the threshold after a confirmed
    /// event; null otherwise.
    /// </summary>
    public ThresholdEvent? Observe(
        DateTimeOffset timestampUtc,
        double celsius,
        double warnCelsius,
        PowerDirection context)
    {
        if (celsius >= warnCelsius)
        {
            if (_aboveSinceUtc is null)
            {
                _aboveSinceUtc = timestampUtc;
                _peakCelsius = celsius;
                _peakContext = context;
            }
            else if (celsius > _peakCelsius)
            {
                _peakCelsius = celsius;
                _peakContext = context;
            }

            if (!_confirmed && timestampUtc - _aboveSinceUtc.Value >= _dwell)
            {
                _confirmed = true;
                OpenEvent = new ThresholdEvent(_aboveSinceUtc.Value, null, _peakCelsius, _peakContext);
            }
            else if (_confirmed)
            {
                OpenEvent = new ThresholdEvent(_aboveSinceUtc!.Value, null, _peakCelsius, _peakContext);
            }

            return null;
        }

        // Dropped below the threshold.
        ThresholdEvent? closed = null;
        if (_confirmed && _aboveSinceUtc is DateTimeOffset start)
        {
            closed = new ThresholdEvent(start, timestampUtc, _peakCelsius, _peakContext);
        }

        _aboveSinceUtc = null;
        _confirmed = false;
        _peakCelsius = 0;
        OpenEvent = null;
        return closed;
    }
}
