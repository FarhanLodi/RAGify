using System.Collections.Concurrent;
using RAGify.Abstractions;

namespace RAGify.Embeddings;

/// <summary>
/// A thread-safe, in-memory implementation of <see cref="IEmbeddingCache"/>.
/// Optionally bounded by a maximum number of entries, evicting the oldest entries
/// (in insertion order) once the cap is exceeded.
/// </summary>
public class InMemoryEmbeddingCache : IEmbeddingCache
{
    #region Private-Members

    private readonly ConcurrentDictionary<string, float[]> _entries = new();
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly int? _maxEntries;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryEmbeddingCache"/> class.
    /// </summary>
    /// <param name="maxEntries">
    /// The maximum number of entries to retain. When <c>null</c>, the cache is unbounded.
    /// When set and exceeded on a <see cref="SetAsync"/>, the oldest entries are evicted
    /// to keep the cache within bounds.
    /// </param>
    public InMemoryEmbeddingCache(int? maxEntries = null)
    {
        if (maxEntries.HasValue && maxEntries.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "maxEntries must be greater than zero when specified.");

        _maxEntries = maxEntries;
    }

    #region Public-Methods

    /// <summary>
    /// Retrieves a cached embedding for the specified key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A completed task whose result is the cached embedding, or <c>null</c> if not present.</returns>
    public Task<float[]?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _entries.TryGetValue(key, out var value)
            ? Task.FromResult<float[]?>(value)
            : Task.FromResult<float[]?>(null);
    }

    /// <summary>
    /// Stores an embedding in the cache under the specified key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="embedding">The embedding vector to cache.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A completed task that represents the asynchronous operation.</returns>
    public Task SetAsync(string key, float[] embedding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var isNew = !_entries.ContainsKey(key);
        _entries[key] = embedding;

        if (isNew)
        {
            _insertionOrder.Enqueue(key);
            EvictIfNeeded();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A completed task that represents the asynchronous operation.</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _entries.Clear();
        while (_insertionOrder.TryDequeue(out _))
        {
            // Drain the insertion-order queue.
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Private-Methods

    /// <summary>
    /// Evicts the oldest entries until the cache is within its configured bound.
    /// </summary>
    private void EvictIfNeeded()
    {
        if (!_maxEntries.HasValue)
            return;

        while (_entries.Count > _maxEntries.Value && _insertionOrder.TryDequeue(out var oldestKey))
        {
            _entries.TryRemove(oldestKey, out _);
        }
    }

    #endregion
}
