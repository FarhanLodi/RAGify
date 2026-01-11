using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.VectorStores;

/// <summary>
/// Pinecone implementation of IVectorStore.
/// </summary>
public class PineconeVectorStore : IVectorStore
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _indexName;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the PineconeVectorStore class.
    /// </summary>
    /// <param name="apiKey">The Pinecone API key.</param>
    /// <param name="indexName">The name of the Pinecone index.</param>
    /// <param name="environment">The Pinecone environment (e.g., "us-east-1-aws").</param>
    /// <param name="httpClient">Optional HttpClient instance. If not provided, a new one will be created.</param>
    public PineconeVectorStore(string apiKey, string indexName, string environment, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _indexName = indexName;
        _baseUrl = $"https://{indexName}-{environment}.svc.pinecone.io";
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Api-Key", apiKey);
    }

    /// <summary>
    /// Initializes a new instance of the PineconeVectorStore class with a custom base URL.
    /// </summary>
    /// <param name="apiKey">The Pinecone API key.</param>
    /// <param name="indexName">The name of the Pinecone index.</param>
    /// <param name="baseUrl">The base URL for the Pinecone index.</param>
    /// <param name="httpClient">Optional HttpClient instance. If not provided, a new one will be created.</param>
    public PineconeVectorStore(string apiKey, string indexName, Uri baseUrl, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _indexName = indexName;
        _baseUrl = baseUrl.ToString().TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Api-Key", apiKey);
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Upserts a single vector into the store.
    /// </summary>
    public async Task UpsertAsync(string vectorId, float[] vector, IReadOnlyDictionary<string, object> metadata, CancellationToken cancellationToken = default)
    {
        var normalized = VectorMath.Normalize(vector);
        
        var request = new
        {
            vectors = new[]
            {
                new
                {
                    id = vectorId,
                    values = normalized,
                    metadata = metadata
                }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/vectors/upsert", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Upserts multiple vectors into the store in batch.
    /// </summary>
    public async Task UpsertBatchAsync(IReadOnlyList<VectorData> vectors, CancellationToken cancellationToken = default)
    {
        var normalizedVectors = vectors.Select(v => new
        {
            id = v.VectorId,
            values = VectorMath.Normalize(v.Vector),
            metadata = v.Metadata
        }).ToArray();

        var request = new { vectors = normalizedVectors };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/vectors/upsert", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Deletes a vector from the store by its ID.
    /// </summary>
    public async Task DeleteAsync(string vectorId, CancellationToken cancellationToken = default)
    {
        var request = new { ids = new[] { vectorId } };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/vectors/delete", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Deletes all vectors associated with a specific document ID.
    /// </summary>
    public async Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            filter = new Dictionary<string, object>
            {
                { "DocumentId", new Dictionary<string, object> { { "$eq", documentId } } }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/vectors/delete", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Searches for similar vectors using cosine similarity.
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        double threshold = 0.0,
        MetadataFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = VectorMath.Normalize(queryVector);

        var filterDict = new Dictionary<string, object>();
        if (filter != null)
        {
            foreach (var kvp in filter.Filters)
            {
                filterDict[kvp.Key] = new Dictionary<string, object> { { "$eq", kvp.Value } };
            }
        }

        var request = new
        {
            vector = normalizedQuery,
            topK = topK,
            includeMetadata = true,
            filter = filterDict.Count > 0 ? filterDict : null
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/query", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PineconeQueryResponse>(cancellationToken: cancellationToken);
        
        if (result?.Matches == null)
            return Array.Empty<VectorSearchResult>();

        return result.Matches
            .Where(m => m.Score >= threshold)
            .Select(m => new VectorSearchResult
            {
                VectorId = m.Id,
                Similarity = m.Score,
                Metadata = m.Metadata ?? new Dictionary<string, object>()
            })
            .ToList();
    }

    /// <summary>
    /// Clears all vectors from the store.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // Pinecone doesn't have a direct clear operation, so we delete all vectors by deleting the index
        // Note: This requires index deletion permissions. For a safer approach, you might want to
        // delete vectors in batches or use metadata filtering.
        var request = new { deleteAll = true };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/vectors/delete", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Gets the total count of vectors stored in the store.
    /// </summary>
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/describe_index_stats", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PineconeIndexStats>(cancellationToken: cancellationToken);
        return result?.TotalVectorCount ?? 0;
    }

    #endregion

    #region Private-Classes

    private class PineconeQueryResponse
    {
        public List<PineconeMatch>? Matches { get; set; }
    }

    private class PineconeMatch
    {
        public string Id { get; set; } = string.Empty;
        public double Score { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class PineconeIndexStats
    {
        public int TotalVectorCount { get; set; }
    }

    #endregion
}

