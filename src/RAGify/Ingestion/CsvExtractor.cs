using System.Text;
using RAGify.Abstractions;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts readable text from CSV and TSV files, emitting one record per line
/// using the first row as a header.
/// </summary>
public class CsvExtractor : IDocumentExtractor
{
    #region Private-Members

    private static readonly string[] SupportedExtensions = { ".csv", ".tsv" };

    #endregion

    #region Public-Methods

    /// <summary>
    /// Determines whether this extractor can handle the specified file path.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if this extractor can handle the file; otherwise, false.</returns>
    public bool CanExtract(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// Extracts readable text content from a CSV or TSV file.
    /// </summary>
    /// <param name="filePath">The path to the CSV/TSV file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var delimiter = GetDelimiter(filePath);
            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            return Format(content, delimiter);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from CSV file: {filePath}. Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts readable text content from a CSV or TSV stream.
    /// </summary>
    /// <param name="stream">The stream containing the CSV/TSV data.</param>
    /// <param name="mimeType">Optional MIME type. Used to detect a TSV delimiter when it indicates tab-separated values.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var delimiter = mimeType != null && mimeType.Contains("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return Format(content, delimiter);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from CSV stream. Error: {ex.Message}", ex);
        }
    }

    #endregion

    #region Private-Methods

    private static char GetDelimiter(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".tsv" ? '\t' : ',';
    }

    private static string Format(string content, char delimiter)
    {
        var rows = ParseCsv(content, delimiter);
        if (rows.Count == 0)
            return string.Empty;

        var header = rows[0];
        var builder = new StringBuilder();

        // A single row (only a header) is emitted as a joined record.
        if (rows.Count == 1)
        {
            return string.Join(" | ", header).Trim();
        }

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var fields = new List<string>();

            for (var j = 0; j < row.Count; j++)
            {
                var headerName = j < header.Count ? header[j] : $"Column{j + 1}";
                fields.Add($"{headerName}: {row[j]}");
            }

            if (fields.Count > 0)
            {
                builder.AppendLine(string.Join(" | ", fields));
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Minimal RFC 4180 style parser that handles quoted fields containing the
    /// delimiter, escaped double-quotes ("") and newlines inside quotes.
    /// </summary>
    private static List<List<string>> ParseCsv(string content, char delimiter)
    {
        var rows = new List<List<string>>();
        var current = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                fieldStarted = true;
            }
            else if (c == delimiter)
            {
                current.Add(field.ToString());
                field.Clear();
                fieldStarted = true;
            }
            else if (c == '\r')
            {
                // Treat CRLF and lone CR as a line break; the \n branch finalizes the row.
                if (i + 1 < content.Length && content[i + 1] == '\n')
                {
                    continue;
                }

                FinalizeRow(rows, current, field, ref fieldStarted);
            }
            else if (c == '\n')
            {
                FinalizeRow(rows, current, field, ref fieldStarted);
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }

        // Flush any trailing field/row that is not terminated by a newline.
        if (fieldStarted || field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            rows.Add(current);
        }

        return rows;
    }

    private static void FinalizeRow(List<List<string>> rows, List<string> current, StringBuilder field, ref bool fieldStarted)
    {
        current.Add(field.ToString());
        field.Clear();
        rows.Add(new List<string>(current));
        current.Clear();
        fieldStarted = false;
    }

    #endregion
}
