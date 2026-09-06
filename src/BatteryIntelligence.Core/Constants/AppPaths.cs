namespace BatteryIntelligence.Core.Constants;

/// <summary>
/// Locations of user data on disk.
/// </summary>
/// <remarks>
/// All user data lives under <c>%LocalAppData%\BatteryIntelligence</c> and never
/// inside the installation directory, so that it survives upgrade and uninstall
/// (specification section 58).
/// </remarks>
public static class AppPaths
{
    /// <summary>Folder name used beneath the local application data root.</summary>
    public const string FolderName = "BatteryIntelligence";

    /// <summary>File name of the settings document.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>File name of the SQLite database.</summary>
    public const string DatabaseFileName = "battery.db";

    /// <summary>Subfolder holding rotated log files.</summary>
    public const string LogsFolderName = "logs";

    /// <summary>
    /// Optional user-editable process-grouping overrides
    /// (docs/monitoring-dataflow.md section 5 — "the grouping table is data").
    /// </summary>
    public const string ProcessGroupsFileName = "process-groups.json";

    /// <summary>
    /// Root data directory, created if it does not exist.
    /// </summary>
    public static string DataDirectory =>
        EnsureDirectory(Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create),
            FolderName));

    /// <summary>Full path of the settings file.</summary>
    public static string SettingsFile => Path.Combine(DataDirectory, SettingsFileName);

    /// <summary>Full path of the optional process-grouping overrides file (may not exist).</summary>
    public static string ProcessGroupsFile => Path.Combine(DataDirectory, ProcessGroupsFileName);

    /// <summary>Directory holding log files, created if it does not exist.</summary>
    public static string LogsDirectory =>
        EnsureDirectory(Path.Combine(DataDirectory, LogsFolderName));

    /// <summary>
    /// Full path of the database, honouring a user-configured directory when one
    /// is set and usable.
    /// </summary>
    /// <param name="configuredDirectory">
    /// The directory from settings, or empty to use the default location.
    /// </param>
    /// <remarks>
    /// A configured directory that cannot be created falls back to the default
    /// rather than failing startup. Losing the preferred location is an
    /// inconvenience; refusing to run is not an acceptable response to it.
    /// </remarks>
    public static string DatabaseFile(string? configuredDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            try
            {
                return Path.Combine(
                    EnsureDirectory(Path.GetFullPath(configuredDirectory)),
                    DatabaseFileName);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException)
            {
                // Fall through to the default location.
            }
        }

        return Path.Combine(DataDirectory, DatabaseFileName);
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
