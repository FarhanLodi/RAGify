namespace RAGify.Abstractions;

/// <summary>
/// Represents the result of a query operation in the RAG system.
/// </summary>
public class QueryResult
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the list of retrieval results containing relevant chunks.
    /// </summary>
    public IReadOnlyList<RetrievalResult> Context { get; set; } = Array.Empty<RetrievalResult>();

    /// <summary>
    /// Gets or sets the metadata about the retrieval process.
    /// </summary>
    public RetrievalMetadata? Metadata { get; set; }

    #endregion
}

/// <summary>
/// Contains metadata about the retrieval process.
/// </summary>
public class RetrievalMetadata
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the effective Top-K value used for retrieval.
    /// </summary>
    public int EffectiveTopK { get; set; }

    /// <summary>
    /// Gets or sets the similarity threshold used for filtering results.
    /// </summary>
    public double SimilarityThreshold { get; set; }

    /// <summary>
    /// Gets or sets the detected question type (e.g., "Fact", "Explanatory", "List").
    /// </summary>
    public string? QuestionType { get; set; }

    /// <summary>
    /// Gets or sets the number of chunks before deduplication was applied.
    /// </summary>
    public int ChunksBeforeDeduplication { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether dynamic Top-K was used.
    /// </summary>
    public bool DynamicTopKUsed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether deduplication was applied.
    /// </summary>
    public bool DeduplicationApplied { get; set; }

    #endregion
}
