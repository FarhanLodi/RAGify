namespace RAGify.Abstractions;

/// <summary>
/// Represents a single reranked document and its relevance score.
/// </summary>
public class RerankResult
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the zero-based index of the document in the original input list.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the relevance score assigned by the reranker. Higher is more relevant.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the document text that was scored.
    /// </summary>
    public string Document { get; set; } = string.Empty;

    #endregion
}
