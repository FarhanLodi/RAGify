namespace RAGify.Abstractions;

/// <summary>
/// Represents a document that can be processed by the RAG system.
/// </summary>
public interface IDocument
{
    #region Public-Members

    /// <summary>
    /// Gets the unique identifier for the document.
    /// </summary>
    string DocumentId { get; }

    /// <summary>
    /// Gets the text content of the document.
    /// </summary>
    string Content { get; }

    /// <summary>
    /// Gets the source identifier of the document (e.g., file name, URL).
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Gets the page number if the document is part of a multi-page document.
    /// </summary>
    int? Page { get; }

    /// <summary>
    /// Gets the metadata associated with the document.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }

    #endregion
}
