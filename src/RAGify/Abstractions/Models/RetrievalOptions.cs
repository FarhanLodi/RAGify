namespace RAGify.Abstractions;

/// <summary>
/// Options for customizing retrieval behavior.
/// </summary>
public class RetrievalOptions
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the maximum number of results to return. Set to 0 to use dynamic Top-K.
    /// </summary>
    public int TopK { get; set; } = 0;

    /// <summary>
    /// Gets or sets the minimum similarity threshold for results (0.0 to 1.0). Default is 0.35.
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.35;

    /// <summary>
    /// Gets or sets a value indicating whether to use dynamic Top-K based on question type.
    /// </summary>
    public bool EnableDynamicTopK { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to apply deduplication to remove similar chunks.
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Gets or sets the optional metadata filter to apply to the search.
    /// </summary>
    public MetadataFilter? Filter { get; set; }

    #endregion
}
