using BatteryIntelligence.Core.Constants;

namespace BatteryIntelligence.Tests.Unit.Configuration;

/// <summary>
/// The data location must be under the user profile, stable, and outside any
/// install directory, so it survives upgrade and the unpackaged→MSIX transition
/// (specification section 58).
/// </summary>
public sealed class AppPathsTests
{
    [Fact]
    public void DataDirectory_IsUnderTheUserProfile_AndNamedForTheApp()
    {
        string dir = AppPaths.DataDirectory;

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.StartsWith(userProfile, dir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppPaths.FolderName, dir, StringComparison.Ordinal);
    }

    [Fact]
    public void DataDirectory_IsStableAcrossCalls()
    {
        Assert.Equal(AppPaths.DataDirectory, AppPaths.DataDirectory);
    }

    [Fact]
    public void SettingsAndLogPaths_LiveUnderTheDataDirectory()
    {
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.SettingsFile, StringComparison.Ordinal);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.LogsDirectory, StringComparison.Ordinal);
        Assert.EndsWith(AppPaths.SettingsFileName, AppPaths.SettingsFile, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseFile_WithNoConfiguredDirectory_UsesTheDefaultLocation()
    {
        string path = AppPaths.DatabaseFile(null);

        Assert.StartsWith(AppPaths.DataDirectory, path, StringComparison.Ordinal);
        Assert.EndsWith(AppPaths.DatabaseFileName, path, StringComparison.Ordinal);
    }

    [Fact]
    public void DataDirectory_ResolvesEvenWithoutTheLocalAppDataEnvVar()
    {
        string? original = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", null);
            string dir = AppPaths.DataDirectory; // must not throw; falls back to the API / user profile
            Assert.False(string.IsNullOrWhiteSpace(dir));
            Assert.EndsWith(AppPaths.FolderName, dir, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", original);
        }
    }
}
