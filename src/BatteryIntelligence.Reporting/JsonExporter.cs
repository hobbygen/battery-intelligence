using System.Text.Json;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Reporting;

/// <summary>
/// Writes an export as indented JSON (specification section 36): a root object
/// <c>{ exportedUtc, range: { fromUtc, toUtc }, tables: { "&lt;name&gt;": [ { col: value, … }, … ] } }</c>.
/// Values are emitted as the strings the data source produced — the data source
/// is the single authority on formatting and rounding, so keeping one lossless
/// representation avoids a class of precision and locale bugs.
/// </summary>
public sealed class JsonExporter : IReportExporter
{
    /// <inheritdoc />
    public string FormatName => "JSON";

    /// <inheritdoc />
    public string FileExtension => ".json";

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public async Task ExportAsync(
        ExportRequest request,
        IReadOnlyList<ExportTable> tables,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(destination);

        await using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("exportedUtc", DateTimeOffset.UtcNow.ToString("O"));

        writer.WriteStartObject("range");
        writer.WriteString("fromUtc", request.Range.FromUtc.ToString("O"));
        writer.WriteString("toUtc", request.Range.ToUtc.ToString("O"));
        writer.WriteEndObject();

        writer.WriteStartObject("tables");
        foreach (ExportTable table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WritePropertyName(table.Name);
            writer.WriteStartArray();

            foreach (IReadOnlyList<string?> row in table.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    string? value = c < row.Count ? row[c] : null;
                    if (value is null)
                    {
                        writer.WriteNull(table.Columns[c]);
                    }
                    else
                    {
                        writer.WriteString(table.Columns[c], value);
                    }
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            // Flush per table so a year-wide export never fully materialises.
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
