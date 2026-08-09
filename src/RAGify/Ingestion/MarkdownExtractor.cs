using System.Text;
using System.Text.RegularExpressions;
using RAGify.Abstractions;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts plain text from Markdown files by stripping common Markdown syntax.
/// </summary>
public class MarkdownExtractor : IDocumentExtractor
{
    #region Private-Members

    private static readonly string[] SupportedExtensions = { ".md", ".markdown" };

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
    /// Extracts plain text content from a Markdown file.
    /// </summary>
    /// <param name="filePath">The path to the Markdown file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted plain text content.</returns>
    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var markdown = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            return StripMarkdown(markdown);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from Markdown file: {filePath}. Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts plain text content from a Markdown stream.
    /// </summary>
    /// <param name="stream">The stream containing the Markdown data.</param>
    /// <param name="mimeType">Optional MIME type (not used for Markdown extraction).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted plain text content.</returns>
    public async Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var markdown = await reader.ReadToEndAsync(cancellationToken);
            return StripMarkdown(markdown);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from Markdown stream. Error: {ex.Message}", ex);
        }
    }

    #endregion

    #region Private-Methods

    private static string StripMarkdown(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

        // Drop fenced code fences (``` lines) but keep the code text inside.
        text = Regex.Replace(text, @"^[ \t]*```[^\n]*$", string.Empty, RegexOptions.Multiline);

        // Images: ![alt](url) -> alt
        text = Regex.Replace(text, @"!\[([^\]]*)\]\([^)]*\)", "$1");

        // Links: [text](url) -> text
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");

        // ATX heading markers at line start.
        text = Regex.Replace(text, @"^[ \t]*#{1,6}[ \t]*", string.Empty, RegexOptions.Multiline);

        // Blockquote markers at line start.
        text = Regex.Replace(text, @"^[ \t]*>[ \t]?", string.Empty, RegexOptions.Multiline);

        // Horizontal rules.
        text = Regex.Replace(text, @"^[ \t]*([-*_])([ \t]*\1){2,}[ \t]*$", string.Empty, RegexOptions.Multiline);

        // Ordered/unordered list bullets at line start.
        text = Regex.Replace(text, @"^[ \t]*(?:[-*+]|\d+\.)[ \t]+", string.Empty, RegexOptions.Multiline);

        // Inline code / backticks.
        text = text.Replace("`", string.Empty);

        // Bold/italic emphasis markers.
        text = Regex.Replace(text, @"(\*\*|__)(.+?)\1", "$2", RegexOptions.Singleline);
        text = Regex.Replace(text, @"(\*|_)(.+?)\1", "$2", RegexOptions.Singleline);

        // Collapse 3+ blank lines to 2.
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    #endregion
}
