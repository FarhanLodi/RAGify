namespace RAGify.Abstractions;

/// <summary>
/// A cache for embedding vectors, keyed by a content-derived string key.
/// Used to avoid re-computing (and re-paying for) embeddings of identical inputs.
/// </summary>
public interface IEmbeddingCache
{
    #region Public-Methods

    /// <summary>
    /// Attempts to retrieve a cached embedding for the specified key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task whose result is the cached embedding, or <c>null</c> if not present.</returns>
    Task<float[]?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an embedding in the cache under the specified key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="embedding">The embedding vector to cache.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetAsync(string key, float[] embedding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    #endregion
}
