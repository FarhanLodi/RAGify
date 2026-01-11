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

    #endregion
}
