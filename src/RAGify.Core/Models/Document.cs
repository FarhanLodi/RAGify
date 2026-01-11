using RAGify.Abstractions;

namespace RAGify.Core;

/// <summary>
/// Represents a document that can be processed by the RAG system.
/// </summary>
public class Document : IDocument
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the unique identifier for the document.
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text content of the document.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source identifier of the document (e.g., file name, URL).
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the page number if the document is part of a multi-page document.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the metadata associated with the document.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    #endregion

    #region Public-Methods

    /// <summary>
    /// Creates a new document from text content.
    /// </summary>
    /// <param name="text">The text content of the document.</param>
    /// <param name="documentId">The unique identifier for the document.</param>
    /// <param name="source">The source identifier (e.g., file name, URL).</param>
    /// <param name="metadata">Optional metadata to associate with the document.</param>
    /// <returns>A new Document instance.</returns>
    public static Document FromText(string text, string documentId, string source, Dictionary<string, object>? metadata = null)
    {
        return new Document
        {
            DocumentId = documentId,
            Content = text,
            Source = source,
            Metadata = metadata ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Creates a new document from a file path.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="documentId">The unique identifier for the document.</param>
    /// <param name="metadata">Optional metadata to associate with the document.</param>
    /// <returns>A new Document instance with the file name as the source.</returns>
    public static Document FromFile(string filePath, string documentId, Dictionary<string, object>? metadata = null)
    {
        var fileName = Path.GetFileName(filePath);
        return new Document
        {
            DocumentId = documentId,
            Content = string.Empty,
            Source = fileName,
            Metadata = metadata ?? new Dictionary<string, object>()
        };
    }

    #endregion
}
