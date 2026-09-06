namespace BatteryIntelligence.Core.Processes;

/// <summary>
/// Turns two cumulative-CPU-time readings for a process into a CPU percentage,
/// with no busy sampling (docs/monitoring-dataflow.md section 5, item 1).
/// </summary>
/// <remarks>
/// Pure and allocation-free. The percentage is core-normalised: 100 means one
/// full logical processor, so the sum across every process can exceed 100 on a
/// multi-core machine — that is expected and is what the attribution weights
/// divide against.
/// </remarks>
public static class ProcessCpuCalculator
{
    /// <summary>
    /// CPU use over the interval, as a percentage of a single logical processor.
    /// </summary>
    /// <param name="cpuDelta">Increase in cumulative kernel + user time since the previous sample.</param>
    /// <param name="wallDelta">Elapsed wall-clock time between the two samples.</param>
    /// <param name="coreCount">Logical processor count, used only to cap a single process at 100 × cores.</param>
    /// <returns>
    /// A value in <c>[0, 100 × coreCount]</c>. Returns 0 for a non-positive wall
    /// delta or a negative CPU delta (a pid reused by the OS between samples).
    /// </returns>
    public static double Percent(TimeSpan cpuDelta, TimeSpan wallDelta, int coreCount)
    {
        if (wallDelta <= TimeSpan.Zero || cpuDelta < TimeSpan.Zero)
        {
            return 0.0;
        }

        double ceiling = 100.0 * Math.Max(1, coreCount);
        double percent = cpuDelta.TotalMilliseconds / wallDelta.TotalMilliseconds * 100.0;
        return Math.Clamp(percent, 0.0, ceiling);
    }
}
