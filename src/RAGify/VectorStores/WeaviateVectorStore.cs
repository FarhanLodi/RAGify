using System.Globalization;
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

        // Build the GraphQL query and POST it to the real Weaviate search endpoint.
        var graphqlQuery = BuildSearchGraphQlQuery(normalizedQuery, topK, filter);

        var requestBody = new { query = graphqlQuery };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/v1/graphql", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return ParseSearchResponse(responseStream, threshold);
    }

    /// <summary>
    /// Builds the GraphQL <c>Get</c> query for a nearVector search, including an
    /// optional <c>where</c> filter and the properties to retrieve.
    /// </summary>
    private string BuildSearchGraphQlQuery(float[] normalizedQuery, int topK, MetadataFilter? filter)
    {
        // The vector literal must be formatted with InvariantCulture so a comma-decimal
        // locale does not corrupt the GraphQL numbers.
        var vectorLiteral = string.Join(", ", normalizedQuery.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

        // Determine which properties to request. Always request DocumentId (declared in
        // the schema) plus any keys referenced by the filter so the result is self-describing.
        var propertyNames = new List<string> { "DocumentId" };
        if (filter != null)
        {
            foreach (var key in filter.Filters.Keys)
            {
                if (!string.IsNullOrEmpty(key) && !propertyNames.Contains(key))
                    propertyNames.Add(key);
            }
        }

        var propertiesSelection = string.Join(" ", propertyNames);

        var whereArgument = BuildWhereArgument(filter);
        var arguments = $"nearVector: {{ vector: [{vectorLiteral}] }}, limit: {topK.ToString(CultureInfo.InvariantCulture)}{whereArgument}";

        return $"{{ Get {{ {_className}({arguments}) {{ {propertiesSelection} _additional {{ id certainty distance }} }} }} }}";
    }

    /// <summary>
    /// Builds the GraphQL <c>where</c> argument fragment for the supplied filter, or an
    /// empty string when no filter is present.
    /// </summary>
    private static string BuildWhereArgument(MetadataFilter? filter)
    {
        if (filter == null || filter.Filters.Count == 0)
            return string.Empty;

        var operands = filter.Filters
            .Select(kvp => $"{{ path: [\"{EscapeGraphQlString(kvp.Key)}\"], operator: Equal, valueText: \"{EscapeGraphQlString(kvp.Value?.ToString() ?? string.Empty)}\" }}")
            .ToList();

        string whereBody;
        if (operands.Count == 1)
        {
            whereBody = operands[0];
        }
        else
        {
            whereBody = $"{{ operator: And, operands: [{string.Join(", ", operands)}] }}";
        }

        return $", where: {whereBody}";
    }

    /// <summary>
    /// Parses the GraphQL search response and projects it into search results,
    /// applying the certainty threshold client-side.
    /// </summary>
    private List<VectorSearchResult> ParseSearchResponse(Stream responseStream, double threshold)
    {
        var results = new List<VectorSearchResult>();

        using var document = JsonDocument.Parse(responseStream);
        var root = document.RootElement;

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return results;

        if (!data.TryGetProperty("Get", out var get) || get.ValueKind != JsonValueKind.Object)
            return results;

        if (!get.TryGetProperty(_className, out var items) || items.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            string vectorId = string.Empty;
            double certainty = 0.0;

            if (item.TryGetProperty("_additional", out var additional) && additional.ValueKind == JsonValueKind.Object)
            {
                if (additional.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                    vectorId = idElement.GetString() ?? string.Empty;

                if (additional.TryGetProperty("certainty", out var certaintyElement) &&
                    certaintyElement.ValueKind == JsonValueKind.Number)
                    certainty = certaintyElement.GetDouble();
            }

            // Apply the threshold client-side.
            if (certainty < threshold)
                continue;

            // Reconstruct the metadata dictionary from every returned scalar property
            // (everything except the GraphQL-internal "_additional" block).
            var metadata = new Dictionary<string, object>();
            foreach (var property in item.EnumerateObject())
            {
                if (property.NameEquals("_additional"))
                    continue;

                var value = ConvertJsonElement(property.Value);
                if (value != null)
                    metadata[property.Name] = value;
            }

            results.Add(new VectorSearchResult
            {
                VectorId = vectorId,
                Similarity = certainty,
                Metadata = metadata
            });
        }

        return results;
    }

    /// <summary>
    /// Converts a JSON property value into a plain CLR object for the metadata dictionary.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                    return longValue;
                return element.GetDouble();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return element.GetRawText();
        }
    }

    /// <summary>
    /// Escapes a string for safe embedding inside a GraphQL double-quoted literal.
    /// </summary>
    private static string EscapeGraphQlString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
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

    private class WeaviateListResponse
    {
        public int TotalResults { get; set; }
    }

    #endregion
}

