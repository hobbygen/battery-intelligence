using System.Globalization;
using BatteryIntelligence.Core.Alerts;
using BatteryIntelligence.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace BatteryIntelligence.App.Services;

/// <summary>
/// Plays the bundled voice clips for battery alerts. The only file in the solution
/// that touches the media playback API.
/// </summary>
/// <remarks>
/// Clips ship in the <c>Sounds</c> folder next to the executable, named
/// <c>Full_Batt_N.(wav|mp3)</c> for "battery full" and <c>Low_Batt_N.(wav|mp3)</c>
/// for "connect the charger" — dropping another file with that pattern into the
/// folder adds it to the picker, no code change. Every call is guarded: a missing
/// file or an audio failure returns <see langword="false"/>, never throws.
/// </remarks>
public sealed class VoiceAlertPlayer : IVoiceAlertPlayer, IDisposable
{
    private static readonly string[] Extensions = [".wav", ".mp3"];

    private readonly ILogger<VoiceAlertPlayer> _logger;
    private readonly string _directory;
    private readonly Lock _sync = new();
    private MediaPlayer? _player;

    public VoiceAlertPlayer(ILogger<VoiceAlertPlayer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _directory = Path.Combine(AppContext.BaseDirectory, "Sounds");
    }

    /// <inheritdoc/>
    public IReadOnlyList<VoiceClip> GetClips(VoiceAlertCue cue)
    {
        string prefix = PrefixFor(cue);
        try
        {
            if (!Directory.Exists(_directory))
            {
                return [];
            }

            return
            [
                .. Directory.EnumerateFiles(_directory, prefix + "*")
                    .Select(p => Path.GetFileName(p))
                    .OfType<string>()
                    .Where(n => Extensions.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
                    .Select(n => (Name: n, Number: NumberOf(n, prefix)))
                    .OrderBy(x => x.Number ?? int.MaxValue)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new VoiceClip(
                        x.Name,
                        x.Number is { } number ? string.Create(CultureInfo.InvariantCulture, $"Voice {number}") : Path.GetFileNameWithoutExtension(x.Name))),
            ];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not list voice clips in {Directory}.", _directory);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<bool> PlayAsync(VoiceAlertCue cue, string? fileName, int volumePercent, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VoiceClip> clips = GetClips(cue);
        VoiceClip? clip = clips.FirstOrDefault(c => string.Equals(c.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            ?? clips.FirstOrDefault();

        if (clip is null)
        {
            _logger.LogDebug("No voice clip installed for {Cue}.", cue);
            return false;
        }

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.Combine(_directory, clip.FileName)).AsTask(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _player ??= new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Alerts };
                _player.Pause();
                _player.Volume = Math.Clamp(volumePercent, 0, 100) / 100.0;
                _player.Source = MediaSource.CreateFromStorageFile(file);
                _player.Play();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to play voice clip {Clip}.", clip.FileName);
            return false;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _player?.Dispose();
            _player = null;
        }
    }

    private static string PrefixFor(VoiceAlertCue cue) => cue == VoiceAlertCue.FullyCharged ? "Full_Batt_" : "Low_Batt_";

    private static int? NumberOf(string fileName, string prefix)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Length > prefix.Length
            && int.TryParse(stem.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            ? n
            : null;
    }
}
