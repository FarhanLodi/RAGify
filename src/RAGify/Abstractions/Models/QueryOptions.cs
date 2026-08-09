namespace RAGify.Abstractions;

/// <summary>
/// Options for customizing query behavior in the RAG system.
/// </summary>
public class QueryOptions
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the retrieval options for the query.
    /// </summary>
    public RetrievalOptions Retrieval { get; set; } = new();

    /// <summary>
    /// Gets or sets the generation options used by <c>AnswerAsync</c> / <c>StreamAnswerAsync</c>.
    /// Ignored by retrieval-only <c>QueryAsync</c>.
    /// </summary>
    public GenerationOptions? Generation { get; set; }

    #endregion
}
