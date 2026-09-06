using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryIntelligence.Core.Constants;
using BatteryIntelligence.Core.Processes;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.ProcessMonitoring;

/// <summary>
/// Builds the effective <see cref="ProcessGroupingTable"/>: the built-in
/// <see cref="ProcessGroupingTable.Default"/>, with any rules from a
/// user-editable <c>process-groups.json</c> prepended so they win
/// (docs/monitoring-dataflow.md section 5 — new applications can be recognised
/// without a rebuild).
/// </summary>
/// <remarks>
/// The file is optional and read once at startup. A missing or malformed file is
/// logged and ignored — it never blocks monitoring.
/// </remarks>
public static class ProcessGroupingTableLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads overrides from <paramref name="overridePath"/> (default: the standard location) and merges them over the built-in table.</summary>
    public static ProcessGroupingTable Load(ILogger logger, string? overridePath = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        string path = overridePath ?? AppPaths.ProcessGroupsFile;
        if (!File.Exists(path))
        {
            return ProcessGroupingTable.Default;
        }

        try
        {
            string json = File.ReadAllText(path);
            FileRule[]? parsed = JsonSerializer.Deserialize<FileRule[]>(json, Options);
            if (parsed is null || parsed.Length == 0)
            {
                return ProcessGroupingTable.Default;
            }

            List<ProcessGroupRule> rules = [];
            foreach (FileRule rule in parsed)
            {
                if (string.IsNullOrWhiteSpace(rule.ApplicationKey))
                {
                    continue;
                }

                rules.Add(new ProcessGroupRule(
                    rule.ApplicationKey!.Trim(),
                    string.IsNullOrWhiteSpace(rule.DisplayName) ? rule.ApplicationKey!.Trim() : rule.DisplayName!.Trim(),
                    rule.ProcessNames ?? [],
                    rule.PathFragments ?? []));
            }

            rules.AddRange(ProcessGroupingTable.Default.Rules);
            logger.LogInformation("Loaded {Count} process-grouping override rule(s) from {Path}.", rules.Count - ProcessGroupingTable.Default.Rules.Count, path);
            return new ProcessGroupingTable(rules);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read process-grouping overrides from {Path}; using the built-in table.", path);
            return ProcessGroupingTable.Default;
        }
    }

    private sealed record FileRule(
        [property: JsonPropertyName("applicationKey")] string? ApplicationKey,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("processNames")] string[]? ProcessNames,
        [property: JsonPropertyName("pathFragments")] string[]? PathFragments);
}
