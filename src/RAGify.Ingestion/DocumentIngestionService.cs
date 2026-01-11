using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Ingestion;

/// <summary>
/// Service for ingesting documents from various sources and formats.
/// </summary>
public class DocumentIngestionService
{
    #region Private-Members

    private readonly IReadOnlyList<IDocumentExtractor> _extractors;
    private readonly ILogger<DocumentIngestionService>? _logger;

    #endregion

    public DocumentIngestionService(IEnumerable<IDocumentExtractor> extractors, ILogger<DocumentIngestionService>? logger = null)
    {
        _extractors = extractors.ToList();
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Creates a DocumentIngestionService with all default extractors (PDF, Word, Excel, HTML, PlainText).
    /// </summary>
    /// <returns>A new DocumentIngestionService instance with default extractors.</returns>
    public static DocumentIngestionService CreateDefault()
    {
        return new DocumentIngestionService(new IDocumentExtractor[]
        {
            new PdfExtractor(),
            new WordExtractor(),
            new ExcelExtractor(),
            new HtmlExtractor(),
            new PlainTextExtractor()
        });
    }

    /// <summary>
    /// Ingests a document from a file path.
    /// </summary>
    /// <param name="filePath">The path to the file to ingest.</param>
    /// <param name="documentId">Optional document ID. If not provided, a GUID will be generated.</param>
    /// <param name="metadata">Optional metadata to associate with the document.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the ingested document.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file is not found.</exception>
    /// <exception cref="NotSupportedException">Thrown when no extractor is available for the file type.</exception>
    public async Task<IDocument> IngestFromFileAsync(
        string filePath,
        string? documentId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Ingesting document from file: {FilePath}", filePath);

        if (!File.Exists(filePath))
        {
            _logger?.LogError("File not found: {FilePath}", filePath);
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var extractor = _extractors.FirstOrDefault(e => e.CanExtract(filePath));
        if (extractor == null)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var supportedExtensions = new[] { ".pdf", ".docx", ".xlsx", ".html", ".htm", ".txt", ".md", ".csv", ".log" };
            
            _logger?.LogError("No extractor found for file: {FilePath} with extension: {Extension}", filePath, extension);
            throw new NotSupportedException(
                $"No extractor found for file: {filePath}\n" +
                $"File extension '{extension}' is not supported.\n" +
                $"Supported extensions: {string.Join(", ", supportedExtensions)}\n" +
                $"To add support for this file type, implement IDocumentExtractor and add it using AddExtractor() or create a custom DocumentIngestionService.");
        }

        _logger?.LogDebug("Using extractor {ExtractorType} for file {FilePath}", extractor.GetType().Name, filePath);
        var content = await extractor.ExtractAsync(filePath, cancellationToken);
        var docId = documentId ?? Guid.NewGuid().ToString();
        var fileName = Path.GetFileName(filePath);

        _logger?.LogInformation("Successfully ingested file {FilePath} as document {DocumentId} with {ContentLength} characters", 
            filePath, docId, content.Length);

        return new Document
        {
            DocumentId = docId,
            Content = content,
            Source = fileName,
            Metadata = metadata ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Ingests a document from a stream.
    /// </summary>
    /// <param name="stream">The stream containing the document data.</param>
    /// <param name="source">The source identifier (e.g., file name, URL).</param>
    /// <param name="documentId">Optional document ID. If not provided, a GUID will be generated.</param>
    /// <param name="mimeType">Optional MIME type to help determine the extraction method.</param>
    /// <param name="metadata">Optional metadata to associate with the document.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the ingested document.</returns>
    /// <exception cref="NotSupportedException">Thrown when no extractor is available for the source.</exception>
    public async Task<IDocument> IngestFromStreamAsync(
        Stream stream,
        string source,
        string? documentId = null,
        string? mimeType = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Ingesting document from stream with source: {Source}, MIME type: {MimeType}", source, mimeType);

        var extractor = _extractors.FirstOrDefault(e => e.CanExtract(source));
        if (extractor == null)
        {
            _logger?.LogError("No extractor found for source: {Source}", source);
            throw new NotSupportedException($"No extractor found for source: {source}");
        }

        _logger?.LogDebug("Using extractor {ExtractorType} for source {Source}", extractor.GetType().Name, source);
        var content = await extractor.ExtractAsync(stream, mimeType, cancellationToken);
        var docId = documentId ?? Guid.NewGuid().ToString();
        var sourceName = source.Contains(Path.DirectorySeparatorChar) || source.Contains('/')
            ? Path.GetFileName(source)
            : source;

        _logger?.LogInformation("Successfully ingested stream from {Source} as document {DocumentId} with {ContentLength} characters", 
            source, docId, content.Length);

        return new Document
        {
            DocumentId = docId,
            Content = content,
            Source = sourceName,
            Metadata = metadata ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Ingests a document from text content.
    /// </summary>
    /// <param name="text">The text content of the document.</param>
    /// <param name="source">The source identifier (e.g., file name, URL).</param>
    /// <param name="documentId">Optional document ID. If not provided, a GUID will be generated.</param>
    /// <param name="metadata">Optional metadata to associate with the document.</param>
    /// <returns>The ingested document.</returns>
    public IDocument IngestFromText(
        string text,
        string source,
        string? documentId = null,
        Dictionary<string, object>? metadata = null)
    {
        _logger?.LogInformation("Ingesting document from text with source: {Source}, text length: {TextLength}", source, text.Length);
        var docId = documentId ?? Guid.NewGuid().ToString();
        var sourceName = source.Contains(Path.DirectorySeparatorChar) || source.Contains('/')
            ? Path.GetFileName(source)
            : source;
        _logger?.LogDebug("Created document {DocumentId} from text", docId);
        return Document.FromText(text, docId, sourceName, metadata);
    }

    #endregion
}
