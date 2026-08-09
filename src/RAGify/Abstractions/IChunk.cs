namespace RAGify.Abstractions;

/// <summary>
/// Represents a chunk of text extracted from a document.
/// </summary>
public interface IChunk
{
    #region Public-Members

    /// <summary>
    /// Gets the unique identifier for the chunk.
    /// </summary>
    string ChunkId { get; }

    /// <summary>
    /// Gets the text content of the chunk.
    /// </summary>
    string Text { get; }

    /// <summary>
    /// Gets the index of the chunk within the document.
    /// </summary>
    int Index { get; }

    /// <summary>
    /// Gets the identifier of the document this chunk belongs to.
    /// </summary>
    string DocumentId { get; }

    /// <summary>
    /// Gets the metadata associated with the chunk.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }

    #endregion
}
