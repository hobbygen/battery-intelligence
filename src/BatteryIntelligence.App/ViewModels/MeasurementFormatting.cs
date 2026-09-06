using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>
/// A value formatted for display alongside whether it needs a grade badge.
/// </summary>
/// <remarks>
/// This is the UI-layer expression of specification section 3: an unavailable
/// value never renders as a plausible-looking number, and only measured values
/// are shown without a badge (docs/ui-navigation.md section 4).
/// </remarks>
/// <param name="Text">What to display — the formatted value, or an explanation when unavailable.</param>
/// <param name="RequiresBadge">Whether a grade badge ("Calculated" / "Estimated") must be shown beside <see cref="Text"/>.</param>
/// <param name="BadgeText">The badge label, empty when <see cref="RequiresBadge"/> is <see langword="false"/>.</param>
public sealed record DisplayValue(string Text, bool RequiresBadge, string BadgeText)
{
    public static DisplayValue Unavailable(string reason) => new(reason, false, string.Empty);
}

/// <summary>Formats <see cref="Measurement{T}"/> values for XAML binding.</summary>
public static class MeasurementFormatting
{
    public static DisplayValue Percentage(Measurement<double> value, string unavailableReason = "Not available") =>
        Format(value, v => $"{v:F0}%", unavailableReason);

    public static DisplayValue MilliwattHours(Measurement<int> value, string unavailableReason = "Not available") =>
        Format(value, v => $"{v:N0} mWh", unavailableReason);

    public static DisplayValue Millivolts(Measurement<int> value, string unavailableReason = "Not available") =>
        Format(value, v => $"{v:N0} mV", unavailableReason);

    public static DisplayValue Milliwatts(Measurement<int> value, string unavailableReason = "Not available") =>
        Format(value, v => $"{(v >= 0 ? "+" : string.Empty)}{v:N0} mW", unavailableReason);

    public static DisplayValue Milliamps(Measurement<double> value, string unavailableReason = "Not available") =>
        Format(value, v => $"{(v >= 0 ? "+" : string.Empty)}{v:F0} mA", unavailableReason);

    public static DisplayValue Celsius(Measurement<double> value, string unavailableReason = "Sensor not exposed by this device") =>
        Format(value, v => $"{v:F1} °C", unavailableReason);

    public static DisplayValue Count(Measurement<int> value, string unavailableReason = "Not reported by firmware") =>
        Format(value, v => v.ToString("N0", CultureInfo.InvariantCulture), unavailableReason);

    public static DisplayValue RetentionPercentage(Measurement<double> value, string unavailableReason = "Not available") =>
        Format(value, v => $"{v:F1}%", unavailableReason);

    public static DisplayValue State(Measurement<BatteryState> value) =>
        Format(value, v => v switch
        {
            BatteryState.Charging => "Charging",
            BatteryState.Discharging => "Discharging",
            BatteryState.Idle => "Idle",
            BatteryState.Full => "Full",
            BatteryState.NotPresent => "Not present",
            _ => "Unknown",
        }, "Unknown");

    public static DisplayValue AcOnline(Measurement<bool> value) =>
        Format(value, v => v ? "Connected" : "Disconnected", "Not available");

    private static DisplayValue Format<T>(Measurement<T> value, Func<T, string> format, string unavailableReason)
        where T : struct
    {
        if (!value.HasValue)
        {
            return DisplayValue.Unavailable(unavailableReason);
        }

        string text = format(value.Value!.Value);
        return value.RequiresBadge
            ? new DisplayValue(text, true, BadgeLabel(value.Quality))
            : new DisplayValue(text, false, string.Empty);
    }

    private static string BadgeLabel(DataQuality quality) => quality switch
    {
        DataQuality.Calculated => "Calculated",
        DataQuality.Estimated => "Estimated",
        DataQuality.Suspect => "Suspect",
        _ => string.Empty,
    };
}
