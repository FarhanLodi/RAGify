namespace RAGify.Abstractions;

/// <summary>
/// Provides functionality to retrieve relevant chunks based on a query.
/// </summary>
public interface IRetrievalEngine
{
    #region Public-Methods

    /// <summary>
    /// Retrieves relevant chunks for a given query using semantic search.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="options">Optional retrieval options for customizing search behavior.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of retrieval results with similarity scores.</returns>
    Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, RetrievalOptions? options = null, CancellationToken cancellationToken = default);

    #endregion
}
