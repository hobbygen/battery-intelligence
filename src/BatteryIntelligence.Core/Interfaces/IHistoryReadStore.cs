using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Reads history for the History page — tier-aware and downsampled
/// (specification section 17; docs/database.md section 6). Declared in Core,
/// implemented in Data, the same seam as <see cref="ISessionStore"/>.
/// </summary>
public interface IHistoryReadStore
{
    /// <summary>
    /// The metric over the requested range, read from the tier
    /// <c>HistoryTierSelector.TierForSpan</c> chooses and min/max-downsampled to
    /// <see cref="HistoryRequest.PointBudget"/>. Empty when the range holds no data.
    /// </summary>
    Task<ChartSeries> GetSeriesAsync(HistoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>The earliest and latest timestamps on record, or nulls when the database is empty.</summary>
    Task<(DateTimeOffset? EarliestUtc, DateTimeOffset? LatestUtc)> GetExtentAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether any <c>TemperatureSample</c> rows exist (so the metric selector can offer temperature).</summary>
    Task<bool> HasTemperatureDataAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Collects the rows an export covers (specification section 36). Declared in
/// Core, implemented in Data; the Reporting project only formats what this yields.
/// </summary>
public interface IExportDataSource
{
    /// <summary>Reads every table named by <see cref="ExportRequest.Scope"/>, with rows inside the range and enum columns written as names.</summary>
    Task<IReadOnlyList<ExportTable>> CollectAsync(ExportRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Formats an export to a stream (specification section 36).</summary>
public interface IReportExporter
{
    /// <summary>Display name, e.g. "CSV".</summary>
    string FormatName { get; }

    /// <summary>File extension including the dot, e.g. ".csv".</summary>
    string FileExtension { get; }

    /// <summary>MIME type, e.g. "text/csv".</summary>
    string ContentType { get; }

    /// <summary>Writes <paramref name="tables"/> to <paramref name="destination"/> incrementally.</summary>
    Task ExportAsync(
        ExportRequest request,
        IReadOnlyList<ExportTable> tables,
        Stream destination,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Deletes all recorded telemetry (specification section 29, "Delete all history
/// with confirmation"). Declared in Core, implemented in Data.
/// </summary>
public interface IHistoryMaintenance
{
    /// <summary>
    /// Empties every telemetry, session, health, insight and alert table in one
    /// transaction, then <c>VACUUM</c>s. Keeps <c>BatteryDevice</c>,
    /// <c>AppSettings</c>, <c>DataRetentionSettings</c> and <c>SchemaMigration</c>.
    /// </summary>
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
