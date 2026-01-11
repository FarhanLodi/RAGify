using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.VectorStores;

/// <summary>
/// Weaviate implementation of IVectorStore.
/// </summary>
public class WeaviateVectorStore : IVectorStore
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _className;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the WeaviateVectorStore class.
    /// </summary>
    /// <param name="baseUrl">The Weaviate server base URL (e.g., "http://localhost:8080").</param>
    /// <param name="className">The name of the class/collection to use.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="httpClient">Optional HttpClient instance. If not provided, a new one will be created.</param>
    public WeaviateVectorStore(string baseUrl, string className = "RAGifyVector", string? apiKey = null, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _className = className;
        _apiKey = apiKey;
        _httpClient = httpClient ?? new HttpClient();
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Upserts a single vector into the store.
    /// </summary>
    public async Task UpsertAsync(string vectorId, float[] vector, IReadOnlyDictionary<string, object> metadata, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaExistsAsync(cancellationToken);

        var normalized = VectorMath.Normalize(vector);
        
        var request = new WeaviateObject
        {
            Id = vectorId,
            Class = _className,
            Vector = normalized,
            Properties = metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/v1/objects", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Upserts multiple vectors into the store in batch.
    /// </summary>
    public async Task UpsertBatchAsync(IReadOnlyList<VectorData> vectors, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaExistsAsync(cancellationToken);

        var batchRequest = new WeaviateBatchRequest
        {
            Objects = vectors.Select(v => new WeaviateObject
            {
                Id = v.VectorId,
                Class = _className,
                Vector = VectorMath.Normalize(v.Vector),
                Properties = v.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            }).ToList()
        };

        var json = JsonSerializer.Serialize(batchRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/v1/batch/objects", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Deletes a vector from the store by its ID.
    /// </summary>
    public async Task DeleteAsync(string vectorId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"{_baseUrl}/v1/objects/{_className}/{vectorId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Deletes all vectors associated with a specific document ID.
    /// </summary>
    public async Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var whereClause = new
        {
            path = new[] { "DocumentId" },
            operatorEnum = "Equal",
            valueString = documentId
        };

        var request = new
        {
            @class = _className,
            where = whereClause
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var requestMessage = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/v1/objects")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
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
        await EnsureSchemaExistsAsync(cancellationToken);

        var normalizedQuery = VectorMath.Normalize(queryVector);

        var whereClause = (object?)null;
        if (filter != null && filter.Filters.Count > 0)
        {
            var conditions = filter.Filters.Select(kvp => new
            {
                path = new[] { kvp.Key },
                operatorEnum = "Equal",
                valueString = kvp.Value.ToString()
            }).ToList();

            if (conditions.Count == 1)
            {
                whereClause = conditions[0];
            }
            else
            {
                whereClause = new
                {
                    operatorEnum = "And",
                    operands = conditions
                };
            }
        }

        var request = new
        {
            @class = _className,
            vector = normalizedQuery,
            limit = topK,
            where = whereClause,
            additional = new
            {
                certainty = threshold
            }
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/v1/query", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WeaviateQueryResponse>(cancellationToken: cancellationToken);
        
        if (result?.Data?.Get == null || result.Data.Get.Items == null)
            return Array.Empty<VectorSearchResult>();

        return result.Data.Get.Items
            .Where(item => item.Additional?.Certainty >= threshold)
            .Select(item => new VectorSearchResult
            {
                VectorId = item.Id ?? string.Empty,
                Similarity = item.Additional?.Certainty ?? 0.0,
                Metadata = item.Properties ?? new Dictionary<string, object>()
            })
            .ToList();
    }

    /// <summary>
    /// Clears all vectors from the store.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var request = new
        {
            @class = _className,
            match = new { @class = _className }
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var requestMessage = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/v1/objects")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Gets the total count of vectors stored in the store.
    /// </summary>
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/v1/objects?class={_className}&limit=0", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WeaviateListResponse>(cancellationToken: cancellationToken);
        return result?.TotalResults ?? 0;
    }

    #endregion

    #region Private-Methods

    private async Task EnsureSchemaExistsAsync(CancellationToken cancellationToken)
    {
        await _initSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Check if schema exists
            var response = await _httpClient.GetAsync($"{_baseUrl}/v1/schema/{_className}", cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Create schema
                var schema = new
                {
                    @class = _className,
                    vectorizer = "none",
                    properties = new[]
                    {
                        new { name = "DocumentId", dataType = new[] { "string" } }
                    }
                };

                var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var createResponse = await _httpClient.PostAsync($"{_baseUrl}/v1/schema", content, cancellationToken);
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

    private class WeaviateObject
    {
        public string Id { get; set; } = string.Empty;
        public string Class { get; set; } = string.Empty;
        public float[]? Vector { get; set; }
        public Dictionary<string, object>? Properties { get; set; }
    }

    private class WeaviateBatchRequest
    {
        public List<WeaviateObject> Objects { get; set; } = new();
    }

    private class WeaviateQueryResponse
    {
        public WeaviateQueryData? Data { get; set; }
    }

    private class WeaviateQueryData
    {
        public WeaviateQueryGet? Get { get; set; }
    }

    private class WeaviateQueryGet
    {
        public List<WeaviateQueryItem>? Items { get; set; }
    }

    private class WeaviateQueryItem
    {
        public string? Id { get; set; }
        public Dictionary<string, object>? Properties { get; set; }
        public WeaviateAdditional? Additional { get; set; }
    }

    private class WeaviateAdditional
    {
        public double Certainty { get; set; }
    }

    private class WeaviateListResponse
    {
        public int TotalResults { get; set; }
    }

    #endregion
}

