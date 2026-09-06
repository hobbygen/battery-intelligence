namespace BatteryIntelligence.Core.Battery;

/// <summary>
/// Detects the "unknown" sentinel values several Windows battery APIs return in
/// place of a real number (quirk Q3, docs/capability-matrix.md section 4).
/// </summary>
/// <remarks>
/// Observed concretely on the reference machine: <c>Win32_Battery.EstimatedRunTime</c>
/// returns <c>71582788</c> (minutes) — <c>0xFFFFFFFF</c> reinterpreted — and
/// <c>BatteryRuntime.EstimatedRuntime</c> returns <c>4294967295</c>. Specification
/// section 63 requires these be rejected before they ever reach storage or the UI,
/// never stored as a plausible-looking number.
/// </remarks>
public static class BatterySentinels
{
    private const uint Sentinel32 = 0xFFFFFFFF;
    private const uint SentinelHigh = 0x80000000;
    private const ushort Sentinel16 = 0xFFFF;

    /// <summary>Whether a raw 32-bit value is a known "unknown" sentinel.</summary>
    public static bool IsSentinel(uint value) => value == Sentinel32 || value == SentinelHigh;

    /// <summary>Whether a raw 16-bit value is a known "unknown" sentinel.</summary>
    public static bool IsSentinel(ushort value) => value == Sentinel16;

    /// <summary>
    /// Whether a signed value is a known sentinel, including <see cref="int.MinValue"/>
    /// (the signed reinterpretation of <see cref="SentinelHigh"/>) and the signed
    /// reinterpretation of <see cref="Sentinel32"/> (<c>-1</c>).
    /// </summary>
    /// <remarks>
    /// <c>-1</c> is deliberately treated as a sentinel here even though it is a
    /// structurally valid signed integer: every quantity this method is applied to
    /// (capacity, rate magnitude, runtime) is physically non-negative, so a
    /// literal <c>-1</c> can only be the unsigned sentinel reinterpreted.
    /// </remarks>
    public static bool IsSentinel(int value) => value == int.MinValue || value == -1;

    /// <summary>Whether a raw 64-bit value is a known sentinel.</summary>
    public static bool IsSentinel(long value) => IsSentinel(unchecked((uint)value)) && (value >> 32) == 0;
}
