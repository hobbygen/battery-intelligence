using BatteryIntelligence.Core.Diagnostics;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Reads recent entries from the application log for the Diagnostics page's log
/// viewer (specification section 26). Declared in Core, implemented in the App
/// where the Serilog file layout is known.
/// </summary>
public interface ILogReader
{
    /// <summary>The folder holding the rolling log files, for an "open folder" action.</summary>
    string LogDirectory { get; }

    /// <summary>
    /// The most recent <paramref name="maxEntries"/> entries from the current log
    /// file, oldest first. Never throws — a read failure yields an empty list.
    /// </summary>
    Task<IReadOnlyList<LogEntry>> ReadRecentAsync(int maxEntries, CancellationToken cancellationToken = default);
}
