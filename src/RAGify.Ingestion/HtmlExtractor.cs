using HtmlAgilityPack;
using RAGify.Abstractions;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts text from HTML files, stripping scripts and styles.
/// </summary>
public class HtmlExtractor : IDocumentExtractor
{
    #region Private-Members

    private static readonly string[] SupportedExtensions = { ".html", ".htm" };

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
    /// Extracts text content from an HTML file.
    /// </summary>
    /// <param name="filePath">The path to the HTML file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content with HTML tags removed.</returns>
    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var htmlDoc = new HtmlDocument();
                htmlDoc.Load(filePath);

                RemoveNodes(htmlDoc.DocumentNode, "script");
                RemoveNodes(htmlDoc.DocumentNode, "style");
                RemoveNodes(htmlDoc.DocumentNode, "noscript");

                var text = htmlDoc.DocumentNode.InnerText;

                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\n\s*\n", "\n\n");

                return text.Trim();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from HTML file: {filePath}. Error: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts text content from an HTML stream.
    /// </summary>
    /// <param name="stream">The stream containing the HTML data.</param>
    /// <param name="mimeType">Optional MIME type (not used for HTML extraction).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content with HTML tags removed.</returns>
    public async Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var htmlDoc = new HtmlDocument();
                htmlDoc.Load(stream);

                RemoveNodes(htmlDoc.DocumentNode, "script");
                RemoveNodes(htmlDoc.DocumentNode, "style");
                RemoveNodes(htmlDoc.DocumentNode, "noscript");

                var text = htmlDoc.DocumentNode.InnerText;

                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\n\s*\n", "\n\n");

                return text.Trim();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from HTML stream. Error: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    #endregion

    #region Private-Methods

    private static void RemoveNodes(HtmlNode node, string tagName)
    {
        var nodesToRemove = node.Descendants(tagName).ToList();
        foreach (var nodeToRemove in nodesToRemove)
        {
            nodeToRemove.Remove();
        }
    }

    #endregion
}
