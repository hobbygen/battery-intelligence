using System.Globalization;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using Microsoft.Extensions.Logging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace BatteryIntelligence.App.Services;

/// <summary>How an export attempt ended.</summary>
public enum ExportStatus
{
    /// <summary>The file was written.</summary>
    Saved,

    /// <summary>The user dismissed the save dialog.</summary>
    Cancelled,

    /// <summary>Something failed while collecting or writing the data.</summary>
    Failed,
}

/// <summary>The outcome of <see cref="ExportService.ExportAsync"/>.</summary>
/// <param name="Status">Whether the export saved, was cancelled, or failed.</param>
/// <param name="Path">The written file's path, when <see cref="ExportStatus.Saved"/>.</param>
/// <param name="Error">A short human-readable reason, when <see cref="ExportStatus.Failed"/>.</param>
public sealed record ExportResult(ExportStatus Status, string? Path = null, string? Error = null);

/// <summary>
/// Runs a report export: a system save-file dialog for the destination, then the
/// selected <see cref="IReportExporter"/> over the rows <see cref="IExportDataSource"/>
/// collects (specification section 36). The destination path only ever comes from
/// the OS picker — the app never writes to a path from configuration or a text box.
/// </summary>
public sealed class ExportService
{
    private readonly IExportDataSource _dataSource;
    private readonly ILogger<ExportService> _logger;

    public ExportService(IExportDataSource dataSource, ILogger<ExportService> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(logger);

        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Prompts for a destination and writes the export there. Never throws — any
    /// failure is returned as <see cref="ExportStatus.Failed"/> with a message.
    /// </summary>
    public async Task<ExportResult> ExportAsync(
        ExportRequest request, IReportExporter exporter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exporter);

        if (App.ShellWindow is not { } window)
        {
            return new ExportResult(ExportStatus.Failed, Error: "The application window is not ready.");
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = SuggestedName(request.Range, exporter.FileExtension),
            };
            picker.FileTypeChoices.Add($"{exporter.FormatName} file", [exporter.FileExtension]);

            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return new ExportResult(ExportStatus.Cancelled);
            }

            IReadOnlyList<ExportTable> tables = await _dataSource.CollectAsync(request, cancellationToken).ConfigureAwait(false);

            // Unpackaged app: the picker grants access to this exact path, so a
            // plain FileStream is the simplest reliable writer.
            await using (var stream = new FileStream(file.Path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await exporter.ExportAsync(request, tables, stream, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Exported {TableCount} table(s) as {Format} to {Path}.", tables.Count, exporter.FormatName, file.Path);
            return new ExportResult(ExportStatus.Saved, file.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed.");
            return new ExportResult(ExportStatus.Failed, Error: ex.Message);
        }
    }

    private static string SuggestedName(DateRange range, string extension) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"battery-history-{range.FromUtc.LocalDateTime:yyyyMMdd}-{range.ToUtc.LocalDateTime:yyyyMMdd}{extension}");
}
