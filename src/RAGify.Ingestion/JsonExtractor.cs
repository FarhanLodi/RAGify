using System.Text;
using System.Text.Json;
using RAGify.Abstractions;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts readable text from JSON and JSONL files by flattening the structure
/// into "key.path: value" lines.
/// </summary>
public class JsonExtractor : IDocumentExtractor
{
    #region Private-Members

    private static readonly string[] SupportedExtensions = { ".json", ".jsonl" };

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
    /// Extracts readable text content from a JSON or JSONL file.
    /// </summary>
    /// <param name="filePath">The path to the JSON/JSONL file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var isJsonLines = Path.GetExtension(filePath).ToLowerInvariant() == ".jsonl";
            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            return Flatten(content, isJsonLines);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from JSON file: {filePath}. Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts readable text content from a JSON or JSONL stream.
    /// </summary>
    /// <param name="stream">The stream containing the JSON/JSONL data.</param>
    /// <param name="mimeType">Optional MIME type. Used to detect JSON Lines when it indicates "jsonl" or "x-ndjson".</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var isJsonLines = mimeType != null &&
                (mimeType.Contains("jsonl", StringComparison.OrdinalIgnoreCase) ||
                 mimeType.Contains("ndjson", StringComparison.OrdinalIgnoreCase));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return Flatten(content, isJsonLines);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from JSON stream. Error: {ex.Message}", ex);
        }
    }

    #endregion

    #region Private-Methods

    private static string Flatten(string content, bool isJsonLines)
    {
        var lines = new List<string>();

        if (isJsonLines)
        {
            var jsonLines = content.Split('\n');
            foreach (var rawLine in jsonLines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                using var document = JsonDocument.Parse(line);
                FlattenElement(document.RootElement, string.Empty, lines);
            }
        }
        else
        {
            using var document = JsonDocument.Parse(content);
            FlattenElement(document.RootElement, string.Empty, lines);
        }

        return string.Join("\n", lines);
    }

    private static void FlattenElement(JsonElement element, string path, List<string> lines)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                    FlattenElement(property.Value, childPath, lines);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var childPath = $"{path}[{index}]";
                    FlattenElement(item, childPath, lines);
                    index++;
                }
                break;

            case JsonValueKind.String:
                lines.Add($"{path}: {element.GetString()}");
                break;

            case JsonValueKind.Number:
                lines.Add($"{path}: {element.GetRawText()}");
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                lines.Add($"{path}: {element.GetRawText()}");
                break;

            case JsonValueKind.Null:
                lines.Add($"{path}: null");
                break;
        }
    }

    #endregion
}
