using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using BatteryIntelligence.Core.Constants;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>A single row on the Diagnostics page.</summary>
/// <param name="Feature">What is being reported.</param>
/// <param name="Status">Its current state.</param>
/// <param name="Source">Where the information came from.</param>
public sealed record DiagnosticEntry(string Feature, string Status, string Source);

/// <summary>A named group of diagnostic rows.</summary>
/// <param name="Title">Group heading.</param>
/// <param name="Entries">Rows in the group.</param>
public sealed record DiagnosticSection(string Title, IReadOnlyList<DiagnosticEntry> Entries);

/// <summary>
/// Backs the Diagnostics page.
/// </summary>
/// <remarks>
/// Specification section 26 makes this page mandatory, and section 47 defines its
/// contents. In this phase it reports the system and application facts that are
/// genuinely knowable. Battery, power, temperature and process capability rows
/// are added as those subsystems are built; they are deliberately absent rather
/// than shown as placeholders, because a row claiming "unknown" for a capability
/// nothing has yet tried to detect would be misleading.
/// </remarks>
public sealed partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<DiagnosticsViewModel> _logger;
    private readonly IBatteryMonitoringService _monitoring;
    private readonly IDatabaseDiagnosticsProvider _databaseDiagnostics;
    private readonly DispatcherQueue _dispatcher;
    private readonly IReadOnlyList<DiagnosticSection> _staticSections;
    private IReadOnlyList<DiagnosticSection> _sections;
    private string _summaryLine = "Running capability checks…";

    public DiagnosticsViewModel(
        ILogger<DiagnosticsViewModel> logger,
        IBatteryMonitoringService monitoring,
        IDatabaseDiagnosticsProvider databaseDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(databaseDiagnostics);

        _logger = logger;
        _monitoring = monitoring;
        _databaseDiagnostics = databaseDiagnostics;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _staticSections = BuildSections();
        _sections = _staticSections;

        _monitoring.Updated += OnMonitoringUpdated;
        _ = RebuildSectionsAsync();
    }

    /// <summary>Diagnostic groups shown on the page.</summary>
    public IReadOnlyList<DiagnosticSection> Sections
    {
        get => _sections;
        private set => SetProperty(ref _sections, value);
    }

    /// <summary>One-line summary shown in the page banner.</summary>
    public string SummaryLine
    {
        get => _summaryLine;
        private set => SetProperty(ref _summaryLine, value);
    }

    public void Dispose() => _monitoring.Updated -= OnMonitoringUpdated;

    private void OnMonitoringUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(() => _ = RebuildSectionsAsync());
    }

    private async Task RebuildSectionsAsync()
    {
        DatabaseDiagnostics database = await SafeGetDatabaseDiagnosticsAsync().ConfigureAwait(true);

        Sections =
        [
            .. _staticSections,
            BuildStorageSection(database),
            BuildBatterySection(_monitoring.Capabilities),
        ];

        CapabilitySnapshot? caps = _monitoring.Capabilities;
        if (caps is null)
        {
            SummaryLine = "Capability detection has not completed yet.";
        }
        else
        {
            int total = caps.Rows.Count;
            int available = caps.Rows.Count(r => r.Available);
            SummaryLine = available == total
                ? $"All {total} battery capabilities are available on this hardware."
                : $"{available} of {total} battery capabilities available. The rest are not exposed by this hardware and the pages that would use them say so rather than showing a fabricated value.";
        }
    }

    private async Task<DatabaseDiagnostics> SafeGetDatabaseDiagnosticsAsync()
    {
        try
        {
            return await _databaseDiagnostics.GetDiagnosticsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read database diagnostics.");
            return new DatabaseDiagnostics(false, AppPaths.DataDirectory, 0, 0, null, null, 0);
        }
    }

    /// <summary>Database facts (specification section 47) — dynamic because size, row count and last-write time change while the app runs.</summary>
    private static DiagnosticSection BuildStorageSection(DatabaseDiagnostics database)
    {
        List<DiagnosticEntry> entries =
        [
            new("Data directory", AppPaths.DataDirectory, "LocalApplicationData"),
            new("Settings file", File.Exists(AppPaths.SettingsFile) ? "Present" : "Not created yet", AppPaths.SettingsFile),
            new("Log directory", AppPaths.LogsDirectory, "LocalApplicationData"),
        ];

        if (!database.Exists)
        {
            entries.Add(new DiagnosticEntry("Database", "Not created yet", database.Path));
        }
        else
        {
            entries.Add(new DiagnosticEntry("Database size", FormatBytes(database.SizeBytes), database.Path));
            entries.Add(new DiagnosticEntry(
                "Battery sample rows",
                database.SampleRowCount.ToString("N0", CultureInfo.InvariantCulture),
                "BatterySample"));
            entries.Add(new DiagnosticEntry(
                "Power sample rows",
                database.PowerSampleRowCount.ToString("N0", CultureInfo.InvariantCulture),
                "PowerSample"));
            entries.Add(new DiagnosticEntry(
                "Temperature sample rows",
                database.TemperatureSampleRowCount.ToString("N0", CultureInfo.InvariantCulture),
                "TemperatureSample — empty on hardware with no sensor"));
            entries.Add(new DiagnosticEntry(
                "Last write",
                database.LastWriteUtc is DateTimeOffset lastWrite ? DescribeAgo(lastWrite) : "Not written yet this session",
                "Write queue"));
            entries.Add(new DiagnosticEntry(
                "Pending writes",
                database.PendingWrites.ToString("N0", CultureInfo.InvariantCulture),
                "Batched, flushes every 30 s or 200 rows"));
            entries.Add(new DiagnosticEntry(
                "Last cleanup",
                database.LastCleanupUtc is DateTimeOffset lastCleanup ? DescribeAgo(lastCleanup) : "Not run yet",
                "Retention service"));
        }

        return new DiagnosticSection("Storage", entries);
    }

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 0.1 ? $"{mb:F1} MB" : $"{bytes:N0} bytes";
    }

    private static string DescribeAgo(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - timestamp;
        return elapsed switch
        {
            { TotalSeconds: < 60 } => "Just now",
            { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes} min ago",
            { TotalHours: < 24 } => $"{(int)elapsed.TotalHours} h ago",
            _ => $"{(int)elapsed.TotalDays} day(s) ago",
        };
    }

    /// <summary>
    /// Renders the live capability matrix (docs/capability-matrix.md section 6).
    /// Every row appears, including unavailable ones — hiding an unavailable row
    /// would defeat the purpose of this page.
    /// </summary>
    private static DiagnosticSection BuildBatterySection(CapabilitySnapshot? capabilities)
    {
        if (capabilities is null)
        {
            return new DiagnosticSection("Battery", [
                new DiagnosticEntry("Capability detection", "Not run yet", "IBatteryCapabilityDetector"),
            ]);
        }

        List<DiagnosticEntry> entries = [.. capabilities.Rows.Select(row => new DiagnosticEntry(
            row.FeatureName,
            row.Available ? DescribeGrade(row.Grade) : "Unavailable",
            row.Detail))];

        return new DiagnosticSection("Battery", entries);
    }

    private static string DescribeGrade(DataQuality? grade) => grade switch
    {
        DataQuality.Measured => "Available (Measured)",
        DataQuality.Calculated => "Available (Calculated)",
        DataQuality.Estimated => "Available (Estimated)",
        DataQuality.Suspect => "Suspect",
        _ => "Available",
    };

    /// <summary>
    /// Copies the diagnostics to the clipboard as plain text.
    /// Specification section 47 requires this.
    /// </summary>
    [RelayCommand]
    private void CopyDiagnostics()
    {
        try
        {
            StringBuilder builder = new();
            builder.AppendLine("Battery Intelligence diagnostics");
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"Captured {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            builder.AppendLine();

            foreach (DiagnosticSection section in Sections)
            {
                builder.AppendLine(section.Title);
                foreach (DiagnosticEntry entry in section.Entries)
                {
                    builder.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"  {entry.Feature,-28} {entry.Status,-32} {entry.Source}");
                }

                builder.AppendLine();
            }

            DataPackage package = new();
            package.SetText(builder.ToString());
            Clipboard.SetContent(package);

            _logger.LogInformation("Diagnostics copied to the clipboard.");
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process. Failing to copy is
            // an inconvenience, not a reason to disturb the user with an error.
            _logger.LogWarning(ex, "Could not copy diagnostics to the clipboard.");
        }
    }

    private static IReadOnlyList<DiagnosticSection> BuildSections()
    {
        Version? appVersion = typeof(DiagnosticsViewModel).Assembly.GetName().Version;
        OperatingSystem os = Environment.OSVersion;

        List<DiagnosticEntry> system =
        [
            new("Operating system", DescribeWindows(os), "Environment.OSVersion"),
            new("OS build", os.Version.Build.ToString(CultureInfo.InvariantCulture), "Environment.OSVersion"),
            new("Architecture", RuntimeInformation.OSArchitecture.ToString(), "RuntimeInformation"),
            new("Process architecture", RuntimeInformation.ProcessArchitecture.ToString(), "RuntimeInformation"),
            new("Logical processors", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture), "Environment"),
        ];

        List<DiagnosticEntry> application =
        [
            new("Application version", appVersion?.ToString() ?? "unknown", "Assembly metadata"),
            new(".NET runtime", RuntimeInformation.FrameworkDescription, "RuntimeInformation"),
            new("Elevated", IsElevated() ? "Yes" : "No (as designed)", "Process token"),
            new("Packaging", "Unpackaged", "Build configuration"),
        ];

        return
        [
            new DiagnosticSection("System", system),
            new DiagnosticSection("Application", application),
        ];
    }

    /// <summary>
    /// Describes the Windows version, distinguishing 10 from 11.
    /// </summary>
    /// <remarks>
    /// Windows 11 reports a major version of 10, so the build number is the only
    /// reliable discriminator: builds at or above 22000 are Windows 11.
    /// </remarks>
    private static string DescribeWindows(OperatingSystem os)
    {
        string family = os.Version.Build >= 22000 ? "Windows 11" : "Windows 10";
        return $"{family} ({os.Version})";
    }

    private static bool IsElevated()
    {
        try
        {
            using System.Security.Principal.WindowsIdentity identity =
                System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal principal = new(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
