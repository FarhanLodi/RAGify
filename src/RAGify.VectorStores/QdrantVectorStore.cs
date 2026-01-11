using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.VectorStores;

/// <summary>
/// Qdrant implementation of IVectorStore using REST API.
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _collectionName;
    private readonly int _vectorSize;
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the QdrantVectorStore class.
    /// </summary>
    /// <param name="host">The Qdrant server host.</param>
    /// <param name="port">The Qdrant server port (default: 6333 for REST).</param>
    /// <param name="collectionName">The name of the collection to use.</param>
    /// <param name="vectorSize">The size of the vectors.</param>
    /// <param name="useHttps">Whether to use HTTPS.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="httpClient">Optional HttpClient instance. If not provided, a new one will be created.</param>
    public QdrantVectorStore(string host, int port = 6333, string collectionName = "ragify_vectors", int vectorSize = 1536, bool useHttps = false, string? apiKey = null, HttpClient? httpClient = null)
    {
        _collectionName = collectionName;
        _vectorSize = vectorSize;
        _baseUrl = $"{(useHttps ? "https" : "http")}://{host}:{port}";
        _httpClient = httpClient ?? new HttpClient();
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }
    }

    /// <summary>
    /// Initializes a new instance of the QdrantVectorStore class with a base URL.
    /// </summary>
    /// <param name="baseUrl">The base URL for the Qdrant server.</param>
    /// <param name="collectionName">The name of the collection to use.</param>
    /// <param name="vectorSize">The size of the vectors.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="httpClient">Optional HttpClient instance. If not provided, a new one will be created.</param>
    public QdrantVectorStore(string baseUrl, string collectionName = "ragify_vectors", int vectorSize = 1536, string? apiKey = null, HttpClient? httpClient = null)
    {
        _collectionName = collectionName;
        _vectorSize = vectorSize;
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Upserts a single vector into the store.
    /// </summary>
    public async Task UpsertAsync(string vectorId, float[] vector, IReadOnlyDictionary<string, object> metadata, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var normalized = VectorMath.Normalize(vector);
        
        var request = new
        {
            points = new[]
            {
                new
                {
                    id = vectorId,
                    vector = normalized,
                    payload = metadata
                }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"{_baseUrl}/collections/{_collectionName}/points", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Upserts multiple vectors into the store in batch.
    /// </summary>
    public async Task UpsertBatchAsync(IReadOnlyList<VectorData> vectors, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var points = vectors.Select(v => new
        {
            id = v.VectorId,
            vector = VectorMath.Normalize(v.Vector),
            payload = v.Metadata
        }).ToArray();

        var request = new { points };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"{_baseUrl}/collections/{_collectionName}/points", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Deletes a vector from the store by its ID.
    /// </summary>
    public async Task DeleteAsync(string vectorId, CancellationToken cancellationToken = default)
    {
        var request = new { points = new[] { vectorId } };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/delete", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Deletes all vectors associated with a specific document ID.
    /// </summary>
    public async Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            filter = new
            {
                must = new[]
                {
                    new
                    {
                        key = "DocumentId",
                        match = new { value = documentId }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/delete", content, cancellationToken);
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
        await EnsureCollectionExistsAsync(cancellationToken);

        var normalizedQuery = VectorMath.Normalize(queryVector);

        var filterObj = (object?)null;
        if (filter != null && filter.Filters.Count > 0)
        {
            var must = filter.Filters.Select(kvp => new
            {
                key = kvp.Key,
                match = new { value = kvp.Value }
            }).ToArray();

            filterObj = new { must };
        }

        var request = new
        {
            vector = normalizedQuery,
            limit = topK,
            score_threshold = threshold,
            filter = filterObj,
            with_payload = true
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/search", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(cancellationToken: cancellationToken);
        
        if (result?.Result == null)
            return Array.Empty<VectorSearchResult>();

        return result.Result
            .Where(r => r.Score >= threshold)
            .Select(r => new VectorSearchResult
            {
                VectorId = r.Id?.ToString() ?? string.Empty,
                Similarity = r.Score,
                Metadata = r.Payload ?? new Dictionary<string, object>()
            })
            .ToList();
    }

    /// <summary>
    /// Clears all vectors from the store.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _httpClient.DeleteAsync($"{_baseUrl}/collections/{_collectionName}", cancellationToken);
            _initSemaphore.Release();
            await EnsureCollectionExistsAsync(cancellationToken);
        }
        catch
        {
            // Collection might not exist, ignore
        }
    }

    /// <summary>
    /// Gets the total count of vectors stored in the store.
    /// </summary>
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/collections/{_collectionName}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<QdrantCollectionInfo>(cancellationToken: cancellationToken);
            return result?.Result?.PointsCount ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region Private-Methods

    private async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken)
    {
        await _initSemaphore.WaitAsync(cancellationToken);
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/collections/{_collectionName}", cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var createRequest = new
                {
                    vectors = new
                    {
                        size = _vectorSize,
                        distance = "Cosine"
                    }
                };

                var json = JsonSerializer.Serialize(createRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var createResponse = await _httpClient.PutAsync($"{_baseUrl}/collections/{_collectionName}", content, cancellationToken);
                createResponse.EnsureSuccessStatusCode();
            }
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    #endregion

    #region Private-Classes

    private class QdrantSearchResponse
    {
        public List<QdrantSearchResult>? Result { get; set; }
    }

    private class QdrantSearchResult
    {
        public object? Id { get; set; }
        public double Score { get; set; }
        public Dictionary<string, object>? Payload { get; set; }
    }

    private class QdrantCollectionInfo
    {
        public QdrantCollectionResult? Result { get; set; }
    }

    private class QdrantCollectionResult
    {
        public int PointsCount { get; set; }
    }

    #endregion
}
