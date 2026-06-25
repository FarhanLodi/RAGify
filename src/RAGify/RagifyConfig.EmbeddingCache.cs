using RAGify.Abstractions;
using RAGify.Embeddings;

namespace RAGify;

/// <summary>
/// Embedding-cache configuration extensions for <see cref="RagifyConfig"/>.
/// </summary>
public partial class RagifyConfig
{
    #region Public-Methods

    /// <summary>
    /// Configures a custom embedding cache. When set, the embedding provider is wrapped with a
    /// <see cref="CachingEmbeddingProvider"/> during build so identical inputs are not re-embedded.
    /// </summary>
    /// <param name="cache">The embedding cache to use.</param>
    /// <param name="cacheNamespace">An optional namespace prefix mixed into every cache key.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithEmbeddingCache(IEmbeddingCache cache, string? cacheNamespace = null)
    {
        _embeddingCache = cache ?? throw new ArgumentNullException(nameof(cache));
        _embeddingCacheNamespace = cacheNamespace;
        return this;
    }

    /// <summary>
    /// Configures an in-memory embedding cache. When set, the embedding provider is wrapped with a
    /// <see cref="CachingEmbeddingProvider"/> during build so identical inputs are not re-embedded.
    /// </summary>
    /// <param name="maxEntries">The maximum number of entries to retain. When <c>null</c>, the cache is unbounded.</param>
    /// <param name="cacheNamespace">An optional namespace prefix mixed into every cache key.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithInMemoryEmbeddingCache(int? maxEntries = null, string? cacheNamespace = null)
    {
        _embeddingCache = new InMemoryEmbeddingCache(maxEntries);
        _embeddingCacheNamespace = cacheNamespace;
        return this;
    }

    #endregion
}
