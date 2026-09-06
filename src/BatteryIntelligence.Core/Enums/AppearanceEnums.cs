namespace BatteryIntelligence.Core.Enums;

/// <summary>User theme choice. Specification section 35, "Appearance".</summary>
public enum ThemePreference
{
    /// <summary>Follow the Windows app theme.</summary>
    System = 0,

    Light = 1,

    Dark = 2,
}

/// <summary>Interface density. Specification section 35, "Appearance".</summary>
public enum DisplayDensity
{
    Comfortable = 0,

    Compact = 1,
}

/// <summary>
/// Logging verbosity, mirroring the levels required by specification section 48.
/// </summary>
public enum LogVerbosity
{
    Trace = 0,

    Debug = 1,

    Information = 2,

    Warning = 3,

    Error = 4,

    Critical = 5,
}
