namespace RAGify.Abstractions;

/// <summary>
/// Re-orders a candidate set of documents by relevance to a query.
/// Used as a second-stage refinement after vector retrieval to improve result ordering.
/// </summary>
public interface IReranker
{
    #region Public-Methods

    /// <summary>
    /// Scores and re-orders the supplied documents by their relevance to the query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="documents">The candidate document texts to rerank, in their original order.</param>
    /// <param name="topK">The maximum number of results to return.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the reranked results ordered by descending relevance score.
    /// Each result references the original document by its index in <paramref name="documents"/>.
    /// </returns>
    Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topK,
        CancellationToken cancellationToken = default);

    #endregion
}
