using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Reads the current state of every present battery device.
/// </summary>
/// <remarks>
/// Declared in Core and implemented in the Battery layer
/// (docs/architecture.md section 2). A ViewModel depends on this interface, never
/// on WinRT, WMI or an IOCTL directly (specification section 73).
/// </remarks>
public interface IBatteryProvider
{
    /// <summary>Name shown in Diagnostics identifying which implementation is active.</summary>
    string Name { get; }

    /// <summary>
    /// Reads every present battery device and returns one snapshot per device.
    /// </summary>
    /// <remarks>
    /// Returns an empty list, never <see langword="null"/> or an exception, when
    /// no battery is present or every source has failed — "no battery detected" is
    /// a legitimate, complete answer (specification section 25).
    /// </remarks>
    Task<IReadOnlyList<BatterySnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default);
}
