using System.Text;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Reporting;

/// <summary>
/// Writes an export as RFC 4180 CSV (specification section 36). One section per
/// <see cref="ExportTable"/>: a <c>#</c> comment naming the table, the column
/// row, then the data rows; sections separated by a blank line. UTF-8 with a BOM
/// and <c>\r\n</c> endings so the file opens cleanly in a spreadsheet.
/// </summary>
public sealed class CsvExporter : IReportExporter
{
    /// <inheritdoc />
    public string FormatName => "CSV";

    /// <inheritdoc />
    public string FileExtension => ".csv";

    /// <inheritdoc />
    public string ContentType => "text/csv";

    /// <inheritdoc />
    public async Task ExportAsync(
        ExportRequest request,
        IReadOnlyList<ExportTable> tables,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(destination);

        // UTF-8 with a BOM: Excel needs it to read non-ASCII correctly.
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        await writer.WriteLineAsync(
            FormattableString.Invariant($"# Battery Intelligence export  {request.Range.FromUtc:yyyy-MM-dd HH:mm}Z  to  {request.Range.ToUtc:yyyy-MM-dd HH:mm}Z")).ConfigureAwait(false);

        for (int t = 0; t < tables.Count; t++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExportTable table = tables[t];

            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync(FormattableString.Invariant($"# {table.Name}  ({table.RowCount} rows)")).ConfigureAwait(false);
            await writer.WriteLineAsync(FormatRow(table.Columns)).ConfigureAwait(false);

            foreach (IReadOnlyList<string?> row in table.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(FormatRow(row)).ConfigureAwait(false);
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FormatRow(IReadOnlyList<string?> fields)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Quote(fields[i]));
        }

        return builder.ToString();
    }

    private static string Quote(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        bool needsQuoting =
            field.Contains(',', StringComparison.Ordinal) ||
            field.Contains('"', StringComparison.Ordinal) ||
            field.Contains('\n', StringComparison.Ordinal) ||
            field.Contains('\r', StringComparison.Ordinal) ||
            char.IsWhiteSpace(field[0]) ||
            char.IsWhiteSpace(field[^1]);

        if (!needsQuoting)
        {
            return field;
        }

        return string.Concat("\"", field.Replace("\"", "\"\"", StringComparison.Ordinal), "\"");
    }
}
