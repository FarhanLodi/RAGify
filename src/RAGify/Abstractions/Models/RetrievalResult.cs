namespace RAGify.Abstractions;

/// <summary>
/// Represents a retrieval result containing a chunk and its similarity score.
/// </summary>
public class RetrievalResult
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the chunk that was retrieved.
    /// </summary>
    public IChunk Chunk { get; set; } = null!;

    /// <summary>
    /// Gets or sets the similarity score between the query and this chunk (0.0 to 1.0).
    /// </summary>
    public double Similarity { get; set; }

    /// <summary>
    /// Gets or sets the source identifier of the document this chunk belongs to.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the page number if the chunk is from a multi-page document.
    /// </summary>
    public int? Page { get; set; }

    #endregion
}
