using System.ComponentModel;
using System.Diagnostics;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.ProcessMonitoring.Interop;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.ProcessMonitoring;

/// <summary>
/// The Windows implementation of <see cref="IProcessEnumerator"/>: one
/// <c>Process.GetProcesses()</c> pass, cumulative CPU time and working set per
/// process, and the foreground process id (docs/api-strategy.md section 2).
/// </summary>
/// <remarks>
/// <para>
/// Static metadata — the executable path and the start time — is resolved once
/// per process and cached by <c>(pid, startTime)</c>, so a process seen on every
/// tick costs no repeated <c>MainModule</c> resolution (the expensive part) and
/// no file I/O on the sample path (docs/monitoring-dataflow.md section 5,
/// items 3 and 4).
/// </para>
/// <para>
/// Protected processes (System, the kernel, other users' sessions) deny
/// <c>TotalProcessorTime</c> without elevation; those are skipped rather than
/// guessed at — this application never requires administrator rights
/// (docs/limitations.md).
/// </para>
/// </remarks>
public sealed class SystemProcessEnumerator : IProcessEnumerator
{
    private readonly ILogger<SystemProcessEnumerator> _logger;
    private readonly Dictionary<(int Pid, long StartTicks), CachedMetadata> _metadata = [];

    private readonly record struct CachedMetadata(string? ExecutablePath, DateTimeOffset? StartTimeUtc);

    public SystemProcessEnumerator(ILogger<SystemProcessEnumerator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public int CoreCount => Environment.ProcessorCount;

    /// <inheritdoc/>
    public IReadOnlyList<ProcessRawSample> Enumerate()
    {
        uint foregroundPid = ForegroundWindowInterop.ForegroundProcessId();

        Process[] processes = Process.GetProcesses();
        List<ProcessRawSample> samples = new(processes.Length);
        HashSet<(int, long)> live = [];

        foreach (Process process in processes)
        {
            try
            {
                TimeSpan cpuTime;
                try
                {
                    cpuTime = process.TotalProcessorTime;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Access denied (a protected process) or the process exited
                    // between GetProcesses() and this read. Either way it cannot
                    // be attributed honestly, so it is left out.
                    continue;
                }

                DateTimeOffset? startTime = TryReadStartTime(process);
                (int, long) key = (process.Id, startTime?.UtcTicks ?? 0L);
                live.Add(key);

                if (!_metadata.TryGetValue(key, out CachedMetadata metadata))
                {
                    metadata = new CachedMetadata(TryReadPath(process), startTime);
                    _metadata[key] = metadata;
                }

                long workingSet;
                try
                {
                    workingSet = process.WorkingSet64;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    workingSet = 0;
                }

                samples.Add(new ProcessRawSample(
                    process.Id,
                    metadata.StartTimeUtc,
                    process.ProcessName,
                    metadata.ExecutablePath,
                    cpuTime,
                    workingSet,
                    foregroundPid != 0 && (uint)process.Id == foregroundPid));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipped a process during enumeration.");
            }
            finally
            {
                process.Dispose();
            }
        }

        PruneMetadata(live);
        return samples;
    }

    private static DateTimeOffset? TryReadStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string? TryReadPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private void PruneMetadata(HashSet<(int, long)> live)
    {
        if (_metadata.Count <= live.Count)
        {
            return;
        }

        List<(int, long)> dead = [];
        foreach ((int, long) key in _metadata.Keys)
        {
            if (!live.Contains(key))
            {
                dead.Add(key);
            }
        }

        foreach ((int, long) key in dead)
        {
            _metadata.Remove(key);
        }
    }
}
