using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BatteryIntelligence.App.Services;

/// <summary>Registers or removes the "start Battery Intelligence when I sign in" hook.</summary>
public interface IStartupService
{
    /// <summary>Whether the app is currently set to launch at sign-in.</summary>
    bool IsEnabled();

    /// <summary>Turns launch-at-sign-in on or off. Never throws — a failure is logged and returned as <see langword="false"/>.</summary>
    bool SetEnabled(bool enabled);
}

/// <summary>
/// <see cref="IStartupService"/> for the **unpackaged** build: a per-user
/// <c>HKCU\…\Run</c> entry, no administrator rights (specification section 23).
/// The packaged build uses the <c>windows.startupTask</c> extension declared in
/// <c>Package.appxmanifest</c> instead; a future packaged-aware implementation
/// would call <c>StartupTask.GetAsync</c> here.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BatteryIntelligence";

    private readonly ILogger<StartupService> _logger;

    public StartupService(ILogger<StartupService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string existing && existing.Contains(ExecutablePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the Run key.");
            return false;
        }
    }

    /// <inheritdoc />
    public bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath()}\"");
                _logger.LogInformation("Enabled launch at sign-in.");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.LogInformation("Disabled launch at sign-in.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update the Run key.");
            return false;
        }
    }

    private static string ExecutablePath() =>
        Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "BatteryIntelligence.exe";
}
