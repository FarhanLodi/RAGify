namespace RAGify.Abstractions;

/// <summary>
/// Provides functionality to store and search vector embeddings.
/// </summary>
public interface IVectorStore
{
    #region Public-Methods

    /// <summary>
    /// Upserts a single vector into the store.
    /// </summary>
    /// <param name="vectorId">The unique identifier for the vector.</param>
    /// <param name="vector">The vector embedding to store.</param>
    /// <param name="metadata">The metadata associated with the vector.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task UpsertAsync(string vectorId, float[] vector, IReadOnlyDictionary<string, object> metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts multiple vectors into the store in batch.
    /// </summary>
    /// <param name="vectors">The vectors to upsert.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task UpsertBatchAsync(IReadOnlyList<VectorData> vectors, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a vector from the store by its ID.
    /// </summary>
    /// <param name="vectorId">The unique identifier of the vector to delete.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DeleteAsync(string vectorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all vectors associated with a specific document ID.
    /// </summary>
    /// <param name="documentId">The document ID whose vectors should be deleted.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for similar vectors using cosine similarity.
    /// </summary>
    /// <param name="queryVector">The query vector to search for.</param>
    /// <param name="topK">The maximum number of results to return.</param>
    /// <param name="threshold">The minimum similarity threshold (0.0 to 1.0).</param>
    /// <param name="filter">Optional metadata filter to apply to the search.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the search results ordered by similarity.</returns>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK, double threshold = 0.0, MetadataFilter? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all vectors from the store.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of vectors stored in the store.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the count of vectors.</returns>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    #endregion
}
