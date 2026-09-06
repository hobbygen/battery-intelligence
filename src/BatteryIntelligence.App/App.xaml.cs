using BatteryIntelligence.App.Services;
using BatteryIntelligence.App.ViewModels;
using BatteryIntelligence.App.Windows;
using BatteryIntelligence.Analytics;
using BatteryIntelligence.Battery;
using BatteryIntelligence.Battery.Sources;
using BatteryIntelligence.Notifications;
using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Constants;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Data;
using BatteryIntelligence.Data.Settings;
using BatteryIntelligence.Data.Sqlite;
using BatteryIntelligence.Power;
using BatteryIntelligence.ProcessMonitoring;
using BatteryIntelligence.Reporting;
using BatteryIntelligence.Sessions;
using BatteryIntelligence.Thermal;
using BatteryIntelligence.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Events;

namespace BatteryIntelligence.App;

/// <summary>
/// Application composition root.
/// </summary>
/// <remarks>
/// Builds the dependency injection container and the generic host, configures
/// logging, loads settings, and creates the main window. Hosted services added in
/// later phases start and stop with the host, so pausing and resuming monitoring
/// is one coordinated operation rather than many independent flags.
/// </remarks>
public partial class App : Application
{
    private IHost? _host;
    private MainWindow? _window;
    private DispatcherQueue? _dispatcherQueue;
    private BatteryMessageWindow? _messageWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// The service provider for the running application.
    /// </summary>
    /// <remarks>
    /// WinUI constructs pages through <see cref="Microsoft.UI.Xaml.Controls.Frame"/>
    /// using their parameterless constructors, so pages resolve their view models
    /// from here. This is the one place the application uses service location, and
    /// it is confined to page constructors.
    /// </remarks>
    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("The host has not been built yet.");

    /// <summary>
    /// The shell window once it exists, for the few services that need a window
    /// handle (e.g. a file-save picker). Resolved lazily rather than injected, so
    /// nothing built during the window's own construction takes a dependency on it.
    /// </summary>
    internal static MainWindow? ShellWindow => (Current as App)?._window;

    /// <inheritdoc/>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = args;

        long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _host = BuildHost();

        // Created before the host starts, and on this UI thread: the message-only
        // window must exist before BatteryMonitoringService (a hosted service)
        // subscribes to it, and its window procedure is only ever pumped by this
        // thread's message loop (docs/api-strategy.md section 2).
        _messageWindow = _host.Services.GetRequiredService<BatteryMessageWindow>();

        ISettingsService settings = _host.Services.GetRequiredService<ISettingsService>();

        // Settings must be loaded before the host starts: several hosted
        // services (battery polling interval, database retention window) read
        // ISettingsService.Current in their own StartAsync, and window creation
        // right below needs theme and geometry too. This is the only blocking
        // wait in startup and reads a single small file.
        settings.LoadAsync().GetAwaiter().GetResult();

        // The schema must exist before any hosted service opens a connection.
        // Like the settings load above, this is a single blocking wait at
        // startup, against a database that (on first run) does not exist yet —
        // it applies V001 in milliseconds, not a heavyweight operation.
        DatabaseMigrator migrator = _host.Services.GetRequiredService<DatabaseMigrator>();
        if (!migrator.MigrateAsync().GetAwaiter().GetResult())
        {
            Log.Warning("Database migration failed; battery history will not be recorded this session.");
        }

        // Register with the OS notification platform before hosted services
        // start, so the alert engine's first evaluation can already deliver a
        // toast. A failure here is non-fatal — alerts fall back to the in-app
        // centre (spec §21).
        _host.Services.GetRequiredService<WindowsToastPresenter>().Register();

        _host.Start();

        Log.Information(
            "Battery Intelligence {Version} starting on {OS}.",
            typeof(App).Assembly.GetName().Version,
            Environment.OSVersion.VersionString);

        _window = _host.Services.GetRequiredService<MainWindow>();
        _window.Closed += OnWindowClosed;

        if (settings.Current.General.StartMinimized && settings.Current.General.MinimizeToTray)
        {
            _window.LaunchHidden();
        }
        else
        {
            _window.Activate();
        }

        // Cold-start budget is < 2 s to interactive (docs/monitoring-dataflow.md
        // §1 / prd.md §6). This measures build-host → migrate → window-activate.
        Log.Information(
            "Application ready in {ElapsedMs} ms.",
            (int)System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
    }

    /// <summary>
    /// Called when a second launch is redirected to this instance.
    /// </summary>
    /// <remarks>
    /// Invoked on a background thread by the activation listener, so the work is
    /// marshalled onto the UI thread before touching the window.
    /// </remarks>
    public void OnRelaunched()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            Log.Information("Second launch redirected to the running instance.");
            _window?.BringToFront();
        });
    }

    private IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "BatteryIntelligence",
        });

        ConfigureLogging(builder);
        ConfigureServices(builder.Services);

        return builder.Build();
    }

    private static void ConfigureLogging(HostApplicationBuilder builder)
    {
        // Settings are not loaded yet, so the file is read directly to pick up a
        // configured log level. A failure here must not prevent startup: logging
        // falls back to Information.
        LogEventLevel level = ReadConfiguredLogLevel();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .Enrich.WithProperty("Version", typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown")
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(AppPaths.LogsDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                // Bounded so logs cannot grow without limit (specification section 48).
                fileSizeLimitBytes: 8 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);
    }

    private static LogEventLevel ReadConfiguredLogLevel()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return LogEventLevel.Information;
            }

            using FileStream stream = File.OpenRead(AppPaths.SettingsFile);
            AppSettings? settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                stream,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                });

            return settings?.Advanced.LogLevel switch
            {
                LogVerbosity.Trace => LogEventLevel.Verbose,
                LogVerbosity.Debug => LogEventLevel.Debug,
                LogVerbosity.Warning => LogEventLevel.Warning,
                LogVerbosity.Error => LogEventLevel.Error,
                LogVerbosity.Critical => LogEventLevel.Fatal,
                _ => LogEventLevel.Information,
            };
        }
        catch (Exception)
        {
            // Any failure reading the level is non-fatal; the default stands.
            return LogEventLevel.Information;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        // Monitoring status registry (Phase 12) — every hosted orchestrator
        // reports its tick outcomes here; the Diagnostics page reads it. Registered
        // first so it is available to inject into the monitoring blocks below.
        services.AddSingleton<IMonitoringStatusRegistry, MonitoringStatusRegistry>();
        services.AddSingleton<ILogReader, LogFileReader>();

        // Adaptive sampling (Phase 13) — the visibility signal the samplers read,
        // and the self-metrics the Diagnostics footprint section shows.
        services.AddSingleton<AppVisibilityState>();
        services.AddSingleton<IAppVisibilityState>(sp => sp.GetRequiredService<AppVisibilityState>());
        services.AddSingleton<ISelfMetrics, SelfMetrics>();

        // Persistence (Phase 3)
        services.AddSingleton<ISqliteConnectionFactory>(sp =>
        {
            ISettingsService settingsService = sp.GetRequiredService<ISettingsService>();
            string path = AppPaths.DatabaseFile(settingsService.Current.Data.DatabaseDirectory);
            return new SqliteConnectionFactory(path);
        });
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<BatterySampleWriteQueue>();
        services.AddSingleton<IBatterySampleWriteQueue>(sp => sp.GetRequiredService<BatterySampleWriteQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<BatterySampleWriteQueue>());
        services.AddSingleton<PowerSampleWriteQueue>();
        services.AddSingleton<IPowerSampleWriteQueue>(sp => sp.GetRequiredService<PowerSampleWriteQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<PowerSampleWriteQueue>());
        services.AddSingleton<TemperatureSampleWriteQueue>();
        services.AddSingleton<ITemperatureSampleWriteQueue>(sp => sp.GetRequiredService<TemperatureSampleWriteQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<TemperatureSampleWriteQueue>());
        services.AddSingleton<ProcessSampleWriteQueue>();
        services.AddSingleton<IProcessSampleWriteQueue>(sp => sp.GetRequiredService<ProcessSampleWriteQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<ProcessSampleWriteQueue>());
        services.AddSingleton<IHealthSnapshotStore, HealthSnapshotStore>();
        services.AddSingleton<IInsightStore, InsightStore>();
        services.AddSingleton<IAnalyticsReadStore, AnalyticsReadStore>();
        services.AddHostedService<DatabaseMaintenanceService>();
        services.AddSingleton<IDatabaseDiagnosticsProvider, DatabaseDiagnosticsProvider>();
        services.AddHostedService<BatteryPersistenceBridge>();

        // Application services
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IWindowStateService, WindowStateService>();

        // Windows
        services.AddSingleton<MainWindow>();
        services.AddSingleton<BatteryMessageWindow>();

        // Battery monitoring (Phase 2)
        services.AddSingleton<WinRtBatterySource>();
        services.AddSingleton<WmiBatterySource>();
        services.AddSingleton<IoctlBatterySource>();
        services.AddSingleton<SystemPowerStatusSource>();
        services.AddSingleton<IBatteryProvider, CompositeBatteryProvider>();
        services.AddSingleton<IBatteryCapabilityDetector, BatteryCapabilityDetector>();
        services.AddSingleton<BatteryMonitoringService>();
        services.AddSingleton<IBatteryMonitoringService>(sp => sp.GetRequiredService<BatteryMonitoringService>());
        services.AddHostedService(sp => sp.GetRequiredService<BatteryMonitoringService>());

        // Sessions (Phase 4)
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<SessionMonitoringService>();
        services.AddSingleton<ISessionMonitoringService>(sp => sp.GetRequiredService<SessionMonitoringService>());
        services.AddHostedService(sp => sp.GetRequiredService<SessionMonitoringService>());

        // Power monitoring (Phase 5) — registered after Sessions so
        // ISessionMonitoringService is available for the "session" chart window.
        services.AddSingleton<PowerMonitoringService>();
        services.AddSingleton<IPowerMonitoringService>(sp => sp.GetRequiredService<PowerMonitoringService>());
        services.AddHostedService(sp => sp.GetRequiredService<PowerMonitoringService>());

        // Temperature monitoring (Phase 6) — rides the battery monitor's readings,
        // whose TemperatureCelsius field is already resolved S4 -> S3.
        services.AddSingleton<ThermalMonitoringService>();
        services.AddSingleton<ITemperatureMonitoringService>(sp => sp.GetRequiredService<ThermalMonitoringService>());
        services.AddHostedService(sp => sp.GetRequiredService<ThermalMonitoringService>());

        // Application usage (Phase 7) — its own slower cadence (the process
        // sampler is the heaviest), registered after Sessions so the open
        // session id and screen state are available for each persisted row.
        services.AddSingleton<IProcessEnumerator, SystemProcessEnumerator>();
        services.AddSingleton<ProcessMonitoringService>();
        services.AddSingleton<IProcessMonitoringService>(sp => sp.GetRequiredService<ProcessMonitoringService>());
        services.AddHostedService(sp => sp.GetRequiredService<ProcessMonitoringService>());

        // Analytics (Phase 8) — runtime estimator rides the battery monitor;
        // AnalyticsService rides session closes + a slow timer. Registered after
        // Sessions and Power so their state is available.
        services.AddSingleton<IInsightProvider, RuleBasedInsightProvider>();
        services.AddSingleton<RuntimeEstimationService>();
        services.AddSingleton<IRuntimeEstimationService>(sp => sp.GetRequiredService<RuntimeEstimationService>());
        services.AddHostedService(sp => sp.GetRequiredService<RuntimeEstimationService>());
        services.AddSingleton<AnalyticsService>();
        services.AddSingleton<IAnalyticsService>(sp => sp.GetRequiredService<AnalyticsService>());
        services.AddHostedService(sp => sp.GetRequiredService<AnalyticsService>());
        services.AddSingleton<IAlertStore, AlertStore>();

        // Alerts (Phase 9) — registered after Analytics and Process so their
        // state feeds the evaluation input. The toast presenter is the only place
        // the WinAppSDK notification API is used; a failure there degrades to the
        // in-app centre silently (spec §21).
        services.AddSingleton<WindowsToastPresenter>();
        services.AddSingleton<INotificationPresenter>(sp => sp.GetRequiredService<WindowsToastPresenter>());
        services.AddSingleton<AlertMonitoringService>();
        services.AddSingleton<IAlertMonitoringService>(sp => sp.GetRequiredService<AlertMonitoringService>());
        services.AddHostedService(sp => sp.GetRequiredService<AlertMonitoringService>());

        // History and reporting (Phase 11) — all read-side. The two exporters are
        // resolved together as IEnumerable<IReportExporter> by the History VM.
        services.AddSingleton<IHistoryReadStore, HistoryReadStore>();
        services.AddSingleton<IExportDataSource, ExportDataSource>();
        services.AddSingleton<IHistoryMaintenance, HistoryMaintenance>();
        services.AddSingleton<IReportExporter, CsvExporter>();
        services.AddSingleton<IReportExporter, JsonExporter>();
        services.AddSingleton<ExportService>();

        // View models
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<BatteryViewModel>();
        services.AddTransient<SessionsViewModel>();
        services.AddTransient<PowerViewModel>();
        services.AddTransient<TemperatureViewModel>();
        services.AddTransient<AppUsageViewModel>();
        services.AddTransient<StatisticsViewModel>();
        services.AddTransient<InsightsViewModel>();
        services.AddTransient<AlertsViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<HistoryViewModel>();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;

        Log.Information("Main window closed; shutting down.");
        Shutdown();
    }

    private void Shutdown()
    {
        try
        {
            _host?.Services.GetService<WindowsToastPresenter>()?.Unregister();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Notification unregister failed during shutdown.");
        }

        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Host did not stop cleanly.");
        }
        finally
        {
            _host?.Dispose();
            _host = null;
            _messageWindow?.Dispose();
            _messageWindow = null;
            Log.CloseAndFlush();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _ = sender;

        // Log before the process dies. The exception is not swallowed: a fault
        // this far up is not something the application can meaningfully continue
        // through, and pretending otherwise would hide real defects.
        Log.Fatal(e.Exception, "Unhandled exception: {Message}", e.Message);
        Log.CloseAndFlush();
    }
}
