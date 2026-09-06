using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>A "label : value" row on the About page.</summary>
/// <param name="Label">What the value describes.</param>
/// <param name="Value">The value.</param>
public sealed record AboutRow(string Label, string Value);

/// <summary>A third-party component and its licence, for the About page.</summary>
/// <param name="Component">The library or SDK.</param>
/// <param name="License">Its licence.</param>
public sealed record LicenseRow(string Component, string License);

/// <summary>
/// Backs the About page: application identity plus a handful of genuinely-known
/// system facts. Everything here is real — nothing is placeholder.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IBatteryMonitoringService _battery;
    private readonly IDatabaseDiagnosticsProvider _databaseDiagnostics;
    private readonly ILogger<AboutViewModel> _logger;

    private IReadOnlyList<AboutRow> _systemRows = [];

    public AboutViewModel(
        IBatteryMonitoringService battery,
        IDatabaseDiagnosticsProvider databaseDiagnostics,
        ILogger<AboutViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(databaseDiagnostics);
        ArgumentNullException.ThrowIfNull(logger);

        _battery = battery;
        _databaseDiagnostics = databaseDiagnostics;
        _logger = logger;

        _systemRows = BuildRows(null);
        _ = LoadAsync();
    }

    /// <summary>Marketing-style version string for the identity card.</summary>
    public string VersionLine
    {
        get
        {
            string informational = typeof(AboutViewModel).Assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
                .InformationalVersion?.Split('+')[0] ?? "1.0.0";
            return string.Create(CultureInfo.InvariantCulture, $"Version {informational} · Windows App SDK 1.8 · MIT licensed");
        }
    }

    /// <summary>
    /// A short summary of what the app cannot know, surfaced on the About page
    /// (docs/limitations.md is "surfaced in the application under About").
    /// </summary>
    public IReadOnlyList<string> Limitations { get; } =
    [
        "Many laptops do not expose a battery temperature sensor to Windows. Where yours does not, the Temperature page says so and collects nothing — CPU temperature is never substituted.",
        "Per-application battery impact is a documented, versioned model (AppEnergyV1), not a measurement. Windows only exposes measured per-process energy with administrator rights, which this app never requires. Every such figure carries an Estimated badge.",
        "Some firmware reports a cycle count of zero on a worn battery. That is treated as unavailable and excluded from the health score rather than shown as a real value.",
        "Runtime, charging-quality and trend estimates need history to be trustworthy. Below their data floor the app shows “Calculating…” or hides the figure, never a guess.",
        "The Diagnostics page shows exactly what your machine reports and where every number comes from.",
    ];

    public IReadOnlyList<AboutRow> SystemRows
    {
        get => _systemRows;
        private set => SetProperty(ref _systemRows, value);
    }

    /// <summary>Third-party components bundled with the app, with their licences (specification section 69).</summary>
    public IReadOnlyList<LicenseRow> Licenses { get; } =
    [
        new("Windows App SDK / WinUI 3", "MIT"),
        new("CommunityToolkit.Mvvm", "MIT"),
        new("Serilog + File / Debug sinks", "Apache-2.0"),
        new("LiveChartsCore.SkiaSharpView", "MIT"),
        new("SkiaSharp", "MIT"),
        new("H.NotifyIcon", "MIT"),
        new("Microsoft.Data.Sqlite", "MIT"),
    ];

    private async Task LoadAsync()
    {
        DatabaseDiagnostics? database = null;
        try
        {
            database = await _databaseDiagnostics.GetDiagnosticsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read database diagnostics for the About page.");
        }

        SystemRows = BuildRows(database);
    }

    private IReadOnlyList<AboutRow> BuildRows(DatabaseDiagnostics? database)
    {
        OperatingSystem os = Environment.OSVersion;
        string family = os.Version.Build >= 22000 ? "Windows 11" : "Windows 10";

        string batteryIdentity = "No battery detected";
        if (_battery.CurrentSnapshots.Count > 0)
        {
            BatteryDevice device = _battery.CurrentSnapshots[0].Device;
            List<string> parts = [];
            if (device.Manufacturer is not null)
            {
                parts.Add(device.Manufacturer);
            }

            if (device.DeviceName is not null)
            {
                parts.Add(device.DeviceName);
            }

            if (device.Chemistry is not null)
            {
                parts.Add(device.Chemistry);
            }

            batteryIdentity = parts.Count == 0 ? "Present (identity not reported)" : string.Join(" · ", parts);
        }

        string dbValue = database is { Exists: true }
            ? string.Create(CultureInfo.InvariantCulture, $"{database.SizeBytes / (1024.0 * 1024.0):F1} MB")
            : "Not created yet";

        return
        [
            new AboutRow("Battery", batteryIdentity),
            new AboutRow("Operating system", string.Create(CultureInfo.InvariantCulture, $"{family} ({os.Version})")),
            new AboutRow("Architecture", RuntimeInformation.OSArchitecture.ToString()),
            new AboutRow(".NET runtime", RuntimeInformation.FrameworkDescription),
            new AboutRow("Database", dbValue),
            new AboutRow("Elevated", "No (as designed — no administrator rights required)"),
        ];
    }
}
