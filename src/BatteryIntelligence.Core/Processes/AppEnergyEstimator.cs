using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Processes;

/// <summary>The four <c>AppEnergyV1</c> attribution weights (docs/estimation-strategy.md section 5, step 3).</summary>
/// <param name="Cpu">Weight on a process's share of CPU time.</param>
/// <param name="Gpu">Weight on GPU share. Not observed on this build (contribution is zero).</param>
/// <param name="Io">Weight on I/O rate. Not observed on this build (contribution is zero).</param>
/// <param name="Foreground">Weight added when a process owns the foreground window.</param>
public readonly record struct AppEnergyWeights(double Cpu, double Gpu, double Io, double Foreground)
{
    /// <summary>The weights as configured (spec section 66 — not constants in code).</summary>
    public static AppEnergyWeights FromSettings(ProcessMonitoringSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new AppEnergyWeights(settings.WeightCpu, settings.WeightGpu, settings.WeightIo, settings.WeightForeground);
    }
}

/// <summary>One application's aggregated activity over a window — the estimator's per-app input.</summary>
/// <param name="ApplicationKey">Grouping key.</param>
/// <param name="DisplayName">Name for the row.</param>
/// <param name="CpuPercent">Summed, core-normalised CPU percentage across the group's processes.</param>
/// <param name="GpuPercent">Summed GPU percentage. Zero on this build.</param>
/// <param name="IoRate">Summed I/O rate (bytes/s, arbitrary scale). Zero on this build.</param>
/// <param name="IsForeground">Whether any process in the group owns the foreground window.</param>
/// <param name="MemoryBytes">Summed working set.</param>
/// <param name="ProcessCount">Live process count in the group.</param>
public sealed record AppActivity(
    string ApplicationKey,
    string DisplayName,
    double CpuPercent,
    double GpuPercent,
    double IoRate,
    bool IsForeground,
    long MemoryBytes,
    int ProcessCount);

/// <summary>
/// <c>AppEnergyV1</c> — the documented, versioned, replaceable per-application
/// attribution model (docs/estimation-strategy.md section 5; specification
/// sections 15 and 55).
/// </summary>
/// <remarks>
/// It does <em>not</em> measure any application's consumption. It ranks
/// applications by likely battery impact and apportions a <em>measured</em> total
/// among them by an activity weight, after separating a non-attributable
/// baseline. Every per-app figure is <see cref="DataQuality.Estimated"/>, shares
/// sum to 100 % of the attributable budget, and the baseline is a distinct slice
/// so nothing is double-counted. Pure.
/// </remarks>
public static class AppEnergyEstimator
{
    /// <summary>The model version stamped on every <c>ProcessSample</c> / <c>ApplicationUsage</c> row.</summary>
    public const string Version = "AppEnergyV1";

    /// <summary>
    /// Attributes a system energy budget across applications.
    /// </summary>
    /// <param name="weights">The activity weights.</param>
    /// <param name="activities">Per-application aggregated activity over the window.</param>
    /// <param name="totalBudgetMw">Mean measured system draw over the window (positive magnitude), or <see langword="null"/> on AC.</param>
    /// <param name="baselineMw">The non-attributable baseline draw.</param>
    /// <param name="baselineConfidence">How much the baseline model has learned.</param>
    /// <param name="topApplicationCount">How many applications are ranked in full before the rest collapse into "Other".</param>
    /// <param name="timestampUtc">Stamped on the result.</param>
    public static AppEnergyAttribution Estimate(
        AppEnergyWeights weights,
        IReadOnlyList<AppActivity> activities,
        int? totalBudgetMw,
        double baselineMw,
        AppEnergyConfidence baselineConfidence,
        int topApplicationCount,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(activities);

        bool absoluteAvailable = totalBudgetMw is int budget && budget > 0;
        double appsBudgetMw = absoluteAvailable
            ? Math.Max(0.0, totalBudgetMw!.Value - Math.Max(0.0, baselineMw))
            : 0.0;

        double totalCpu = activities.Sum(a => Math.Max(0.0, a.CpuPercent));
        double totalGpu = activities.Sum(a => Math.Max(0.0, a.GpuPercent));
        double totalIo = activities.Sum(a => Math.Max(0.0, a.IoRate));

        var weighted = new List<(AppActivity Activity, double Weight)>(activities.Count);
        double weightSum = 0.0;
        foreach (AppActivity activity in activities)
        {
            double cpuShare = totalCpu > 0 ? Math.Max(0.0, activity.CpuPercent) / totalCpu : 0.0;
            double gpuShare = totalGpu > 0 ? Math.Max(0.0, activity.GpuPercent) / totalGpu : 0.0;
            double ioShare = totalIo > 0 ? Math.Max(0.0, activity.IoRate) / totalIo : 0.0;

            double w = (cpuShare * weights.Cpu)
                + (gpuShare * weights.Gpu)
                + (ioShare * weights.Io)
                + (activity.IsForeground ? weights.Foreground : 0.0);

            weighted.Add((activity, w));
            weightSum += w;
        }

        var entries = new List<AppUsageEntry>(activities.Count);
        foreach ((AppActivity activity, double weight) in weighted)
        {
            double share = weightSum > 0 ? weight / weightSum : 0.0;
            entries.Add(new AppUsageEntry(
                activity.ApplicationKey,
                activity.DisplayName,
                Math.Round(activity.CpuPercent, 2),
                activity.MemoryBytes,
                activity.IsForeground,
                Math.Round(share * 100.0, 2),
                absoluteAvailable ? (int)Math.Round(appsBudgetMw * share) : null,
                activity.ProcessCount));
        }

        entries.Sort(static (x, y) =>
        {
            int byPower = (y.EstimatedPowerMw ?? 0).CompareTo(x.EstimatedPowerMw ?? 0);
            return byPower != 0 ? byPower : y.SharePercent.CompareTo(x.SharePercent);
        });

        IReadOnlyList<AppUsageEntry> ranked = Collapse(entries, Math.Max(1, topApplicationCount));

        int? baselinePowerMw = absoluteAvailable
            ? (int)Math.Round(Math.Min(Math.Max(0.0, baselineMw), totalBudgetMw!.Value))
            : null;
        double baselineShareOfWhole = absoluteAvailable && totalBudgetMw!.Value > 0
            ? Math.Round(Math.Min(1.0, Math.Max(0.0, baselineMw) / totalBudgetMw.Value) * 100.0, 2)
            : 0.0;

        AppUsageEntry baseline = new(
            AppUsageEntry.BaselineKey,
            "System baseline",
            0,
            0,
            false,
            baselineShareOfWhole,
            baselinePowerMw,
            0,
            IsBaseline: true);

        return new AppEnergyAttribution(
            ranked,
            baseline,
            absoluteAvailable ? totalBudgetMw : null,
            absoluteAvailable,
            baselineConfidence,
            Version,
            timestampUtc);
    }

    private static IReadOnlyList<AppUsageEntry> Collapse(IReadOnlyList<AppUsageEntry> ranked, int topN)
    {
        if (ranked.Count <= topN)
        {
            return ranked;
        }

        var kept = new List<AppUsageEntry>(topN + 1);
        for (int i = 0; i < topN; i++)
        {
            kept.Add(ranked[i]);
        }

        double cpu = 0, share = 0;
        long memory = 0;
        int? power = null;
        int processes = 0;
        bool anyForeground = false;
        for (int i = topN; i < ranked.Count; i++)
        {
            AppUsageEntry tail = ranked[i];
            cpu += tail.CpuPercent;
            share += tail.SharePercent;
            memory += tail.MemoryBytes;
            processes += tail.ProcessCount;
            anyForeground |= tail.IsForeground;
            if (tail.EstimatedPowerMw is int mw)
            {
                power = (power ?? 0) + mw;
            }
        }

        kept.Add(new AppUsageEntry(
            AppUsageEntry.OtherKey,
            "Other applications",
            Math.Round(cpu, 2),
            memory,
            anyForeground,
            Math.Round(share, 2),
            power,
            processes,
            IsOther: true));

        return kept;
    }
}
