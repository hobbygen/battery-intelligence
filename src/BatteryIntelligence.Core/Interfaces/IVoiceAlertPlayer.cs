using BatteryIntelligence.Core.Alerts;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>One selectable voice clip: the file name persisted in settings and a label for the picker.</summary>
public sealed record VoiceClip(string FileName, string DisplayName);

/// <summary>
/// Plays the bundled voice clips for battery alerts ("battery full", "connect the
/// charger"). Declared in Core, implemented in App (the only place a media API is
/// touched), same seam shape as <see cref="INotificationPresenter"/>.
/// </summary>
/// <remarks>
/// Like a toast, a voice clip is best-effort: a missing file or an audio failure
/// returns <see langword="false"/> rather than throwing, and the alert itself is
/// already persisted and raised in-app regardless.
/// </remarks>
public interface IVoiceAlertPlayer
{
    /// <summary>The clips shipped for <paramref name="cue"/>, in display order. Empty when none are installed.</summary>
    IReadOnlyList<VoiceClip> GetClips(VoiceAlertCue cue);

    /// <summary>
    /// Plays <paramref name="fileName"/> for <paramref name="cue"/>, falling back to
    /// the first clip for that cue when the named one is missing. Volume is 0–100.
    /// Returns <see langword="false"/> if nothing could be played.
    /// </summary>
    Task<bool> PlayAsync(VoiceAlertCue cue, string? fileName, int volumePercent, CancellationToken cancellationToken = default);
}
