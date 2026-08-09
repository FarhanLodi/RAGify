using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using RAGify.Abstractions;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts readable text from a web page by fetching the URL and stripping
/// scripts, styles and HTML markup.
/// </summary>
public class WebPageExtractor : IDocumentExtractor, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    #endregion

    #region Constructors-and-Factories

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPageExtractor"/> class.
    /// </summary>
    /// <param name="httpClient">Optional HttpClient to use. If not provided, an internally owned client is created and disposed with this instance.</param>
    public WebPageExtractor(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Determines whether this extractor can handle the specified source.
    /// </summary>
    /// <param name="source">The source (URL) to check.</param>
    /// <returns>True if the source is an HTTP or HTTPS URL; otherwise, false.</returns>
    public bool CanExtract(string source)
    {
        return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fetches a web page and extracts its readable text content.
    /// </summary>
    /// <param name="url">The URL of the web page to fetch and extract.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content with HTML markup removed.</returns>
    public async Task<string> ExtractAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            return CleanHtml(html);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from web page: {url}. Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts readable text content from an already-fetched HTML stream.
    /// </summary>
    /// <param name="stream">The stream containing the HTML data.</param>
    /// <param name="mimeType">Optional MIME type (not used for web page extraction).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content with HTML markup removed.</returns>
    public async Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var html = await reader.ReadToEndAsync(cancellationToken);
            return CleanHtml(html);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from web page stream. Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Releases the resources used by this extractor, disposing the HttpClient when it was created internally.
    /// </summary>
    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region Private-Methods

    private static string CleanHtml(string html)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);

        RemoveNodes(htmlDoc.DocumentNode, "script");
        RemoveNodes(htmlDoc.DocumentNode, "style");
        RemoveNodes(htmlDoc.DocumentNode, "noscript");

        var text = htmlDoc.DocumentNode.InnerText;
        text = HtmlEntity.DeEntitize(text);

        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"\n\s*\n", "\n\n");

        return text.Trim();
    }

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
