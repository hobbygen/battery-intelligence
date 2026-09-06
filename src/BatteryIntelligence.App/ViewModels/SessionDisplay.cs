using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>An immutable, display-ready projection of one <see cref="BatterySessionInfo"/>.</summary>
public sealed class SessionDisplay
{
    public required string Title { get; init; }

    public required string TimeRange { get; init; }

    public required string Duration { get; init; }

    public required string PercentageChange { get; init; }

    public required string ScreenBreakdown { get; init; }

    public required string Interruptions { get; init; }

    public required string Status { get; init; }

    public required bool IsOpen { get; init; }

    public static SessionDisplay From(BatterySessionInfo info, DateTimeOffset nowUtc)
    {
        DateTimeOffset end = info.EndUtc ?? nowUtc;
        TimeSpan duration = end - info.StartUtc;

        string percentageChange = info.StartPercentage is double start && info.EndPercentage is double endPct
            ? $"{start:F0}% → {endPct:F0}%"
            : info.StartPercentage is double startOnly
                ? $"{startOnly:F0}% → …"
                : "Not available";

        return new SessionDisplay
        {
            Title = info.Type == SessionType.Charging ? "Charging" : "Discharging",
            TimeRange = info.EndUtc is DateTimeOffset endUtc
                ? $"{info.StartUtc.ToLocalTime():t} – {endUtc.ToLocalTime():t}"
                : $"Started {info.StartUtc.ToLocalTime():t}",
            Duration = FormatDuration(duration),
            PercentageChange = percentageChange,
            ScreenBreakdown = $"Screen ON {FormatDuration(TimeSpan.FromSeconds(info.ScreenOnSeconds))} · " +
                               $"Screen OFF {FormatDuration(TimeSpan.FromSeconds(info.ScreenOffSeconds))} · " +
                               $"Sleep {FormatDuration(TimeSpan.FromSeconds(info.SleepSeconds))}",
            Interruptions = info.Interruptions == 1 ? "1 interruption" : $"{info.Interruptions} interruptions",
            Status = info.IsOpen
                ? "In progress"
                : info.ClosedCleanly
                    ? DescribeEndReason(info.EndReason)
                    : "Interrupted (application restart)",
            IsOpen = info.IsOpen,
        };
    }

    private static string DescribeEndReason(SessionEndReason? reason) => reason switch
    {
        SessionEndReason.ReachedFull => "Reached full charge",
        SessionEndReason.ChargingStopped => "Charging stopped",
        SessionEndReason.ChargerDisconnected => "Charger disconnected",
        SessionEndReason.ChargerConnected => "Charger connected",
        SessionEndReason.BatteryRemoved => "Battery removed",
        SessionEndReason.Interrupted => "Interrupted",
        _ => "Ended",
    };

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)span.TotalMinutes}m";
        }

        return $"{Math.Max(0, (int)span.TotalSeconds)}s";
    }
}
