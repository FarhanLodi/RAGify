using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.VectorStores;

/// <summary>
/// In-memory implementation of IVectorStore for development and testing.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    #region Private-Members

    private readonly Dictionary<string, (float[] Vector, IReadOnlyDictionary<string, object> Metadata)> _vectors = new();
    private readonly object _lock = new();
    private readonly ILogger<InMemoryVectorStore>? _logger;

    #endregion

    /// <summary>
    /// Initializes a new instance of the InMemoryVectorStore.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    public InMemoryVectorStore(ILogger<InMemoryVectorStore>? logger = null)
    {
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Upserts a single vector into the store.
    /// </summary>
    /// <param name="vectorId">The unique identifier for the vector.</param>
    /// <param name="vector">The vector embedding to store.</param>
    /// <param name="metadata">The metadata associated with the vector.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task UpsertAsync(string vectorId, float[] vector, IReadOnlyDictionary<string, object> metadata, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Upserting vector {VectorId} with dimension {Dimension}", vectorId, vector.Length);
        lock (_lock)
        {
            var normalized = VectorMath.Normalize(vector);
            _vectors[vectorId] = (normalized, metadata);
        }
        _logger?.LogDebug("Successfully upserted vector {VectorId}", vectorId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Upserts multiple vectors into the store in batch.
    /// </summary>
    /// <param name="vectors">The vectors to upsert.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task UpsertBatchAsync(IReadOnlyList<VectorData> vectors, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Upserting batch of {VectorCount} vectors", vectors.Count);
        foreach (var vectorData in vectors)
        {
            await UpsertAsync(vectorData.VectorId, vectorData.Vector, vectorData.Metadata, cancellationToken);
        }
        _logger?.LogInformation("Successfully upserted batch of {VectorCount} vectors", vectors.Count);
    }

    /// <summary>
    /// Deletes a vector from the store by its ID.
    /// </summary>
    /// <param name="vectorId">The unique identifier of the vector to delete.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task DeleteAsync(string vectorId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _vectors.Remove(vectorId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes all vectors associated with a specific document ID.
    /// </summary>
    /// <param name="documentId">The document ID whose vectors should be deleted.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var keysToRemove = _vectors
                .Where(kvp => kvp.Value.Metadata.TryGetValue("DocumentId", out var docId) && docId?.ToString() == documentId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _vectors.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Searches for similar vectors using cosine similarity.
    /// </summary>
    /// <param name="queryVector">The query vector to search for.</param>
    /// <param name="topK">The maximum number of results to return.</param>
    /// <param name="threshold">The minimum similarity threshold (0.0 to 1.0).</param>
    /// <param name="filter">Optional metadata filter to apply to the search.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the search results ordered by similarity.</returns>
    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        double threshold = 0.0,
        MetadataFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Searching for top {TopK} vectors with threshold {Threshold}", topK, threshold);
        var normalizedQuery = VectorMath.Normalize(queryVector);
        var results = new List<VectorSearchResult>();

        lock (_lock)
        {
            foreach (var (vectorId, (vector, metadata)) in _vectors)
            {
                if (filter != null && !MatchesFilter(metadata, filter))
                    continue;

                var similarity = VectorMath.CosineSimilarity(normalizedQuery, vector);

                if (similarity < threshold)
                    continue;

                results.Add(new VectorSearchResult
                {
                    VectorId = vectorId,
                    Similarity = similarity,
                    Metadata = metadata
                });
            }
        }

        var sortedResults = results
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .ToList();

        _logger?.LogInformation("Search completed, found {ResultCount} results from {TotalVectors} vectors", 
            sortedResults.Count, _vectors.Count);

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(sortedResults);
    }

    /// <summary>
    /// Clears all vectors from the store.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Clearing all vectors from in-memory store");
        lock (_lock)
        {
            _vectors.Clear();
        }
        _logger?.LogInformation("Successfully cleared all vectors");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the total count of vectors stored in the store.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the count of vectors.</returns>
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_vectors.Count);
        }
    }

    #endregion

    #region Private-Methods

    private static bool MatchesFilter(IReadOnlyDictionary<string, object> metadata, MetadataFilter filter)
    {
        foreach (var (key, value) in filter.Filters)
        {
            if (!metadata.TryGetValue(key, out var metadataValue))
                return false;

            if (!Equals(metadataValue, value))
                return false;
        }

        return true;
    }

    #endregion
}
