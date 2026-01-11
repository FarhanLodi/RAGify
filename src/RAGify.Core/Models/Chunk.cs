using RAGify.Abstractions;

namespace RAGify.Core;

/// <summary>
/// Represents a chunk of text extracted from a document.
/// </summary>
public class Chunk : IChunk
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the unique identifier for the chunk.
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text content of the chunk.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the index of the chunk within the document.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the document this chunk belongs to.
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the metadata associated with the chunk.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    #endregion

    #region Public-Methods

    /// <summary>
    /// Creates a new chunk with the specified parameters.
    /// </summary>
    /// <param name="text">The text content of the chunk.</param>
    /// <param name="index">The index of the chunk within the document.</param>
    /// <param name="documentId">The identifier of the document this chunk belongs to.</param>
    /// <param name="metadata">Optional metadata to associate with the chunk.</param>
    /// <returns>A new Chunk instance.</returns>
    public static Chunk Create(string text, int index, string documentId, Dictionary<string, object>? metadata = null)
    {
        return new Chunk
        {
            ChunkId = $"{documentId}_chunk_{index}",
            Text = text,
            Index = index,
            DocumentId = documentId,
            Metadata = metadata ?? new Dictionary<string, object>()
        };
    }

    #endregion
}
