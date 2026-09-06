using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BatteryIntelligence.Core.Diagnostics;

/// <summary>
/// Parses the Serilog file sink's output into <see cref="LogEntry"/> records. The
/// template is fixed in <c>App.xaml.cs</c>:
/// <c>{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}</c>.
/// Pure — unit-tested without a file.
/// </summary>
public static partial class LogLineParser
{
    [GeneratedRegex(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<lvl>[A-Z]{3})\] (?<rest>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    /// <summary>
    /// Parses <paramref name="lines"/> in order. A line that matches the header
    /// pattern starts a new entry; any other line (an exception stack trace, a
    /// wrapped message) is appended to the entry above it. Leading lines with no
    /// header are ignored.
    /// </summary>
    public static IReadOnlyList<LogEntry> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<LogEntry> entries = [];
        DateTimeOffset? currentTs = null;
        string? currentLevel = null;
        StringBuilder? currentText = null;

        void Flush()
        {
            if (currentLevel is not null && currentText is not null)
            {
                entries.Add(new LogEntry(currentTs, currentLevel, currentText.ToString().TrimEnd()));
            }
        }

        foreach (string line in lines)
        {
            Match match = HeaderPattern().Match(line);
            if (match.Success)
            {
                Flush();

                currentTs = DateTimeOffset.TryParseExact(
                    match.Groups["ts"].Value,
                    "yyyy-MM-dd HH:mm:ss.fff zzz",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTimeOffset ts)
                    ? ts
                    : null;
                currentLevel = NormaliseLevel(match.Groups["lvl"].Value);
                currentText = new StringBuilder(match.Groups["rest"].Value.Trim());
            }
            else if (currentText is not null && !string.IsNullOrWhiteSpace(line))
            {
                currentText.Append('\n').Append(line.TrimEnd());
            }
        }

        Flush();
        return entries;
    }

    private static string NormaliseLevel(string u3) => u3 switch
    {
        "VRB" => "TRACE",
        "DBG" => "DEBUG",
        "INF" => "INFO",
        "WRN" => "WARNING",
        "ERR" => "ERROR",
        "FTL" => "FATAL",
        _ => u3,
    };
}
