using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Processes;

/// <summary>
/// Estimates the non-attributable <em>baseline</em> draw — display backlight,
/// radios, chipset idle — that <c>AppEnergyV1</c> must separate before dividing
/// the rest among processes (docs/estimation-strategy.md section 5, step 2).
/// </summary>
/// <remarks>
/// <para>
/// A simplified regression against screen state: the baseline for a screen state
/// is an exponentially-weighted moving average of the measured system draw during
/// periods when nothing is doing meaningful work (system CPU below a floor).
/// Screen-off-idle draw is platform idle; the screen-on-idle excess over that is
/// the display estimate — but the attribution only needs the total for the
/// current state, so that split is descriptive, not load-bearing.
/// </para>
/// <para>
/// Until at least <see cref="MinObservationsPerState"/> idle periods have been
/// seen for the state being asked about, a conservative fixed default is returned
/// and the whole attribution is marked <see cref="AppEnergyConfidence.Low"/>.
/// Pure and stateful; the owning service serialises access.
/// </para>
/// </remarks>
public sealed class BaselineEstimator
{
    /// <summary>Idle periods per screen state before the learned baseline is trusted.</summary>
    public const int MinObservationsPerState = 20;

    private const double Alpha = 0.1;

    private readonly double _defaultBaselineMw;

    private double _screenOnIdleMw;
    private int _screenOnCount;
    private double _screenOffIdleMw;
    private int _screenOffCount;

    /// <param name="defaultBaselineMw">Conservative fallback until enough idle history exists.</param>
    public BaselineEstimator(double defaultBaselineMw)
    {
        _defaultBaselineMw = Math.Max(0.0, defaultBaselineMw);
    }

    /// <summary>Idle observations recorded for the screen-on state.</summary>
    public int ScreenOnObservations => _screenOnCount;

    /// <summary>Idle observations recorded for the screen-off state.</summary>
    public int ScreenOffObservations => _screenOffCount;

    /// <summary>
    /// Feeds one tick's measurement to the model. Ignored unless the system was
    /// idle (nothing to attribute) and the draw is a usable positive magnitude.
    /// </summary>
    /// <param name="drawMw">Measured system draw magnitude in milliwatts (sign ignored).</param>
    /// <param name="systemIdle">Whether total system CPU was below the idle floor this tick.</param>
    /// <param name="screenOn">Whether the display was on.</param>
    public void Observe(double drawMw, bool systemIdle, bool screenOn)
    {
        if (!systemIdle)
        {
            return;
        }

        double magnitude = Math.Abs(drawMw);
        if (magnitude <= 0.0 || double.IsNaN(magnitude))
        {
            return;
        }

        if (screenOn)
        {
            _screenOnIdleMw = _screenOnCount == 0 ? magnitude : (Alpha * magnitude) + ((1 - Alpha) * _screenOnIdleMw);
            _screenOnCount++;
        }
        else
        {
            _screenOffIdleMw = _screenOffCount == 0 ? magnitude : (Alpha * magnitude) + ((1 - Alpha) * _screenOffIdleMw);
            _screenOffCount++;
        }
    }

    /// <summary>The baseline draw and its confidence for the given screen state.</summary>
    public (double BaselineMw, AppEnergyConfidence Confidence) Estimate(bool screenOn)
    {
        if (screenOn)
        {
            if (_screenOnCount < MinObservationsPerState)
            {
                return (_defaultBaselineMw, AppEnergyConfidence.Low);
            }

            // Both states learned means display and platform idle are separable.
            AppEnergyConfidence confidence = _screenOffCount >= MinObservationsPerState
                ? AppEnergyConfidence.High
                : AppEnergyConfidence.Medium;
            return (_screenOnIdleMw, confidence);
        }

        return _screenOffCount < MinObservationsPerState
            ? (_defaultBaselineMw, AppEnergyConfidence.Low)
            : (_screenOffIdleMw, AppEnergyConfidence.High);
    }
}
