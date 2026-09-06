using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Probes what this machine can actually report about its battery, once, on
/// demand.
/// </summary>
/// <remarks>
/// Specification section 26 makes the resulting Diagnostics rows mandatory.
/// Compile-time availability of an API is not runtime availability
/// (docs/api-strategy.md section 5), so this performs one real read of each
/// metric rather than reporting what is theoretically possible.
/// </remarks>
public interface IBatteryCapabilityDetector
{
    /// <summary>
    /// Detects capabilities now. Re-detection is required after resume — docking,
    /// undocking and hot-swappable batteries all change the answer.
    /// </summary>
    Task<CapabilitySnapshot> DetectAsync(CancellationToken cancellationToken = default);
}
