using BatteryIntelligence.Core.Constants;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.App.Services;

/// <summary>
/// Reads the newest Serilog rolling file for the Diagnostics log viewer
/// (specification section 26). The sink writes with <c>shared: true</c>, so the
/// file is opened <see cref="FileShare.ReadWrite"/> while Serilog still holds it.
/// </summary>
public sealed class LogFileReader : ILogReader
{
    private readonly ILogger<LogFileReader> _logger;

    public LogFileReader(ILogger<LogFileReader> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public string LogDirectory => AppPaths.LogsDirectory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LogEntry>> ReadRecentAsync(int maxEntries, CancellationToken cancellationToken = default)
    {
        try
        {
            string? newest = new DirectoryInfo(AppPaths.LogsDirectory)
                .EnumerateFiles("app-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;

            if (newest is null)
            {
                return [];
            }

            await using var stream = new FileStream(
                newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            IReadOnlyList<LogEntry> all = LogLineParser.Parse(content.Split('\n'));
            return maxEntries > 0 && all.Count > maxEntries
                ? [.. all.Skip(all.Count - maxEntries)]
                : all;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the log file for the Diagnostics viewer.");
            return [];
        }
    }
}
