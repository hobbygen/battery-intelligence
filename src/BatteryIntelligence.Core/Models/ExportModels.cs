using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>One history chart query (specification section 17).</summary>
/// <param name="Metric">Which metric to chart.</param>
/// <param name="Range">The time window.</param>
/// <param name="PointBudget">
/// Maximum points after min/max-preserving downsampling. Defaults to the
/// docs/monitoring-dataflow.md section 7 chart budget (600).
/// </param>
public sealed record HistoryRequest(HistoryMetric Metric, DateRange Range, int PointBudget = 600);

/// <summary>What to export (specification section 36).</summary>
/// <param name="Range">The time window rows must fall in.</param>
/// <param name="Scope">Which tables to include.</param>
public sealed record ExportRequest(DateRange Range, ExportScope Scope);

/// <summary>
/// One flat, already-stringified table for an export. The exporters only format
/// this — they never touch SQLite and never interpret a column's meaning
/// (docs/architecture.md section 7, "IReportExporter").
/// </summary>
/// <param name="Name">Table name, e.g. "BatterySample".</param>
/// <param name="Columns">Column headers, in order.</param>
/// <param name="Rows">Each row is one value per column; a null is an empty / missing cell.</param>
public sealed record ExportTable(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows)
{
    /// <summary>Row count.</summary>
    public int RowCount => Rows.Count;
}
