using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Alerts;

/// <summary>
/// Which spoken alerts exist. A voice clip is chosen per cue, not per alert type:
/// low and critical battery both mean "plug the charger in", so they share one.
/// </summary>
public enum VoiceAlertCue
{
    /// <summary>The battery reached the fully-charged threshold — unplug.</summary>
    FullyCharged,

    /// <summary>The battery is low or critical — connect the charger.</summary>
    NeedsCharger,
}

/// <summary>Pure mapping from a fired alert to its voice cue (if any).</summary>
public static class VoiceAlertCues
{
    /// <summary>The cue to speak for <paramref name="type"/>, or <see langword="null"/> when that alert has no voice.</summary>
    public static VoiceAlertCue? For(AlertType type) => type switch
    {
        AlertType.FullyCharged => VoiceAlertCue.FullyCharged,
        AlertType.LowBattery or AlertType.CriticalBattery => VoiceAlertCue.NeedsCharger,
        _ => null,
    };

    /// <summary>
    /// When one evaluation fires several voiced alerts at once, the one to speak:
    /// critical outranks low, and anything outranks nothing. Only one clip plays
    /// per evaluation so two voices never talk over each other.
    /// </summary>
    public static int Priority(AlertType type) => type switch
    {
        AlertType.CriticalBattery => 3,
        AlertType.LowBattery => 2,
        AlertType.FullyCharged => 1,
        _ => 0,
    };
}
