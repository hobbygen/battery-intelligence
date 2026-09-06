using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Constants;
using BatteryIntelligence.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Data.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON under the application data folder.
/// </summary>
/// <remarks>
/// <para>
/// Settings are treated as untrusted input. A missing, unreadable, malformed or
/// partially corrupt file never prevents startup: the service falls back to
/// validated defaults and logs the reason.
/// </para>
/// <para>
/// Writes are atomic. The document is written to a temporary file and then moved
/// over the target, so an interruption mid-write cannot leave a truncated
/// settings file behind.
/// </para>
/// </remarks>
public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Enums are written as names so the file stays readable and survives
        // enum renumbering.
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppSettings _current = CreateDefaults();
    private bool _disposed;

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
        : this(logger, AppPaths.SettingsFile)
    {
    }

    /// <summary>
    /// Creates a service reading and writing a specific file. Used by tests.
    /// </summary>
    public JsonSettingsService(ILogger<JsonSettingsService> logger, string filePath)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _logger = logger;
        _filePath = filePath;
    }

    /// <inheritdoc/>
    public AppSettings Current => _current;

    /// <inheritdoc/>
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current = await ReadOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged(null);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(
        Action<AppSettings> mutate,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            mutate(_current);
            _current.Validate();
            await WriteAsync(_current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged(category);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current.Validate();
            await WriteAsync(_current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppSettings> ReadOrDefaultAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation(
                "No settings file at {Path}; starting with defaults.", _filePath);
            return CreateDefaults();
        }

        try
        {
            await using FileStream stream = File.OpenRead(_filePath);
            AppSettings? loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is null)
            {
                _logger.LogWarning(
                    "Settings file at {Path} deserialized to null; using defaults.", _filePath);
                return CreateDefaults();
            }

            loaded.Validate();
            _logger.LogInformation("Settings loaded from {Path}.", _filePath);
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException
                                    or IOException
                                    or UnauthorizedAccessException)
        {
            // A corrupt settings file must not stop the application. Preserve it
            // for diagnosis rather than overwriting it silently, and continue on
            // defaults.
            _logger.LogError(
                ex, "Could not read settings from {Path}; using defaults.", _filePath);
            TryPreserveCorruptFile();
            return CreateDefaults();
        }
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _filePath + ".tmp";

        try
        {
            await using (FileStream stream = File.Create(temporaryPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failing to persist settings is not fatal; the in-memory values
            // remain correct for this session.
            _logger.LogError(ex, "Could not write settings to {Path}.", _filePath);

            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(cleanup, "Could not remove temporary settings file.");
            }
        }
    }

    private void TryPreserveCorruptFile()
    {
        try
        {
            string backup = $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(_filePath, backup, overwrite: true);
            _logger.LogWarning("Preserved unreadable settings file as {Path}.", backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not preserve unreadable settings file.");
        }
    }

    private void RaiseChanged(string? category) =>
        Changed?.Invoke(this, new SettingsChangedEventArgs(_current, category));

    private static AppSettings CreateDefaults()
    {
        AppSettings settings = new();
        settings.Validate();
        return settings;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
