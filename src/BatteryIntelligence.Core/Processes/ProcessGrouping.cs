using System.Globalization;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Processes;

/// <summary>
/// Resolves a raw process observation to an <c>(ApplicationKey, DisplayName)</c>
/// pair, so many processes fold into one application row
/// (docs/monitoring-dataflow.md section 5; specification section 15).
/// </summary>
/// <remarks>
/// Pure. Resolution order (first match wins): executable-path fragment, then
/// process name, then a normalised fallback derived from the process name itself
/// so an unrecognised application still groups its own child processes together.
/// </remarks>
public static class ProcessGrouping
{
    /// <summary>Resolves one sample against <paramref name="table"/>.</summary>
    public static (string ApplicationKey, string DisplayName) Resolve(ProcessRawSample sample, ProcessGroupingTable table)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(table);

        string name = Normalise(sample.ProcessName);
        string? path = sample.ExecutablePath?.ToLowerInvariant();

        if (path is not null)
        {
            foreach (ProcessGroupRule rule in table.Rules)
            {
                foreach (string fragment in rule.PathFragments)
                {
                    if (fragment.Length > 0 && path.Contains(fragment.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        return (rule.ApplicationKey, rule.DisplayName);
                    }
                }
            }
        }

        foreach (ProcessGroupRule rule in table.Rules)
        {
            foreach (string ruleName in rule.ProcessNames)
            {
                if (string.Equals(Normalise(ruleName), name, StringComparison.Ordinal))
                {
                    return (rule.ApplicationKey, rule.DisplayName);
                }
            }
        }

        return (name.Length == 0 ? "unknown" : name, ToDisplayName(sample.ProcessName));
    }

    private static string Normalise(string processName)
    {
        string trimmed = processName.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.ToLowerInvariant();
    }

    private static string ToDisplayName(string processName)
    {
        string normalised = Normalise(processName);
        if (normalised.Length == 0)
        {
            return "Unknown";
        }

        // "someapp" -> "Someapp"; leave names that already look intentional alone.
        return normalised.Contains(' ', StringComparison.Ordinal)
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalised)
            : char.ToUpperInvariant(normalised[0]) + normalised[1..];
    }
}
