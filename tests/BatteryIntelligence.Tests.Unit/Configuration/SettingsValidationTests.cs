using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Data.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Configuration;

/// <summary>
/// Settings are treated as untrusted input: the file can be hand-edited or
/// corrupted, so values are repaired rather than trusted (specification
/// section 46). Traceability: R-070.
/// </summary>
public sealed class SettingsValidationTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        AppSettings settings = new();
        settings.Validate();

        Assert.Equal(5, settings.Monitoring.PowerSampleSeconds);
        Assert.Equal(20, settings.Alerts.LowBatteryPercent);
        Assert.Equal(10, settings.Alerts.CriticalBatteryPercent);
        Assert.True(settings.General.MinimizeToTray);
    }

    [Fact]
    public void SamplingIntervals_AreClampedToSaneBounds()
    {
        // A settings file asking for millisecond sampling would make the monitor
        // consume more power than it measures.
        AppSettings settings = new();
        settings.Monitoring.PowerSampleSeconds = 0;
        settings.Monitoring.ProcessSampleSeconds = -50;
        settings.Monitoring.TemperatureSampleSeconds = 99999;

        settings.Validate();

        Assert.InRange(settings.Monitoring.PowerSampleSeconds, 1, 300);
        Assert.InRange(settings.Monitoring.ProcessSampleSeconds, 5, 600);
        Assert.InRange(settings.Monitoring.TemperatureSampleSeconds, 2, 600);
    }

    [Fact]
    public void ProcessMonitoringWeights_AreClamped_AndAtLeastOneActivityTermSurvives()
    {
        AppSettings settings = new();
        settings.Processes.WeightCpu = -3.0;
        settings.Processes.WeightGpu = 0.0;
        settings.Processes.WeightIo = 0.0;
        settings.Processes.TopApplicationCount = 1;
        settings.Processes.DefaultBaselineMw = -100;

        settings.Validate();

        Assert.True(settings.Processes.WeightCpu + settings.Processes.WeightGpu + settings.Processes.WeightIo > 0.0);
        Assert.InRange(settings.Processes.TopApplicationCount, 5, 200);
        Assert.Equal(0, settings.Processes.DefaultBaselineMw);
    }

    [Fact]
    public void AlertRetention_IsClampedAndKeptWellAboveRawTelemetry()
    {
        AppSettings settings = new();
        settings.Data.AlertRetentionDays = 1;
        settings.Validate();
        Assert.InRange(settings.Data.AlertRetentionDays, 7, 3650);

        AppSettings big = new();
        big.Data.AlertRetentionDays = 99999;
        big.Validate();
        Assert.Equal(3650, big.Data.AlertRetentionDays);
    }

    [Fact]
    public void AnalyticsThresholds_AreClampedToSaneRanges()
    {
        AppSettings settings = new();
        settings.Analytics.InsightConfidenceThreshold = 0.1;
        settings.Analytics.HealthSnapshotIntervalMinutes = 100_000;
        settings.Analytics.DegradationMinSpanDays = 1;
        settings.Analytics.ChargingQualityMinSessions = 1;

        settings.Validate();

        Assert.InRange(settings.Analytics.InsightConfidenceThreshold, 0.5, 0.99);
        Assert.InRange(settings.Analytics.HealthSnapshotIntervalMinutes, 5, 1440);
        Assert.InRange(settings.Analytics.DegradationMinSpanDays, 7, 365);
        Assert.InRange(settings.Analytics.ChargingQualityMinSessions, 3, 50);
    }

    [Fact]
    public void CriticalThreshold_IsForcedBelowLowThreshold()
    {
        // Otherwise the two alerts fight, and the user gets a critical warning
        // before a low one.
        AppSettings settings = new();
        settings.Alerts.LowBatteryPercent = 15;
        settings.Alerts.CriticalBatteryPercent = 40;

        settings.Validate();

        Assert.True(settings.Alerts.CriticalBatteryPercent < settings.Alerts.LowBatteryPercent);
    }

    [Fact]
    public void RetentionTiers_AreOrderedSoDataIsNotLostBeforeRollup()
    {
        AppSettings settings = new();
        settings.Data.RawRetentionDays = 120;
        settings.Data.MinuteRetentionDays = 30;
        settings.Data.HourRetentionDays = 10;

        settings.Validate();

        Assert.True(settings.Data.MinuteRetentionDays >= settings.Data.RawRetentionDays);
        Assert.True(settings.Data.HourRetentionDays >= settings.Data.MinuteRetentionDays);
    }

    [Fact]
    public void DailyRetention_ZeroMeansForever_AndIsPreserved()
    {
        AppSettings settings = new();
        settings.Data.DailyRetentionDays = 0;

        settings.Validate();

        Assert.Equal(0, settings.Data.DailyRetentionDays);
    }

    [Fact]
    public void WindowSize_IsClampedToTheMinimumUsableWindow()
    {
        AppSettings settings = new();
        settings.WindowState.Width = 100;
        settings.WindowState.Height = 50;

        settings.Validate();

        Assert.Equal(WindowStateSettings.MinimumWidth, settings.WindowState.Width);
        Assert.Equal(WindowStateSettings.MinimumHeight, settings.WindowState.Height);
    }

    [Fact]
    public void DisablingEveryNotificationChannel_KeepsTheInAppCentre()
    {
        // Alerts firing into nothing would be worse than no alerts at all.
        AppSettings settings = new();
        settings.Notifications.Enabled = true;
        settings.Notifications.UseWindowsNotifications = false;
        settings.Notifications.UseInAppAlerts = false;

        settings.Validate();

        Assert.True(settings.Notifications.UseInAppAlerts);
    }

    [Fact]
    public void UndefinedEnumValues_FallBackToDefaults()
    {
        AppSettings settings = new();
        settings.Appearance.Theme = (ThemePreference)99;
        settings.Advanced.LogLevel = (LogVerbosity)77;

        settings.Validate();

        Assert.Equal(ThemePreference.System, settings.Appearance.Theme);
        Assert.Equal(LogVerbosity.Information, settings.Advanced.LogLevel);
    }
}

/// <summary>
/// A corrupt or missing settings file must never prevent startup.
/// Traceability: R-070, and specification section 62 "corrupt configuration".
/// </summary>
public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bi-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "settings.json");

    public JsonSettingsServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task MissingFile_LoadsDefaults()
    {
        ISettingsService service = Create();

        await service.LoadAsync();

        Assert.Equal(ThemePreference.System, service.Current.Appearance.Theme);
    }

    [Fact]
    public async Task CorruptFile_LoadsDefaultsAndPreservesTheOriginal()
    {
        await File.WriteAllTextAsync(FilePath, "{ this is not valid json ]]]");

        ISettingsService service = Create();
        await service.LoadAsync();

        Assert.Equal(ThemePreference.System, service.Current.Appearance.Theme);

        // The unreadable file is kept for diagnosis rather than silently deleted.
        string[] preserved = Directory.GetFiles(_directory, "*.corrupt-*");
        Assert.Single(preserved);
    }

    [Fact]
    public async Task RoundTrip_PersistsAndReloads()
    {
        ISettingsService writer = Create();
        await writer.LoadAsync();
        await writer.UpdateAsync(s => s.Appearance.Theme = ThemePreference.Dark, "Appearance");

        ISettingsService reader = Create();
        await reader.LoadAsync();

        Assert.Equal(ThemePreference.Dark, reader.Current.Appearance.Theme);
    }

    [Fact]
    public async Task Update_ValidatesBeforePersisting()
    {
        ISettingsService service = Create();
        await service.LoadAsync();

        await service.UpdateAsync(s => s.Monitoring.PowerSampleSeconds = 0);

        Assert.True(service.Current.Monitoring.PowerSampleSeconds >= 1);
    }

    [Fact]
    public async Task Update_RaisesChangedWithTheCategory()
    {
        ISettingsService service = Create();
        await service.LoadAsync();

        string? observed = null;
        service.Changed += (_, e) => observed = e.Category;

        await service.UpdateAsync(s => s.General.StartMinimized = true, "General");

        Assert.Equal("General", observed);
    }

    private JsonSettingsService Create() =>
        new(NullLogger<JsonSettingsService>.Instance, FilePath);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup failure is irrelevant to the test outcome.
        }
    }
}
