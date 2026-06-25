using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RAGify.Abstractions;

namespace RAGify.Reranking;

/// <summary>
/// Reranker backed by the Cohere Rerank API. Scores documents against a query and
/// returns them ordered by descending relevance.
/// </summary>
public class CohereReranker : IReranker, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly bool _disposeHttpClient;
    private readonly ILogger<CohereReranker>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="CohereReranker"/> class.
    /// </summary>
    /// <param name="apiKey">Cohere API key.</param>
    /// <param name="model">Rerank model name (default: "rerank-english-v3.0").</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api.cohere.ai/v1/).</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, a new one is created and owned by this instance.</param>
    /// <param name="logger">Optional logger.</param>
    public CohereReranker(
        string apiKey,
        string model = "rerank-english-v3.0",
        string? baseUrl = null,
        HttpClient? httpClient = null,
        ILogger<CohereReranker>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;

        _httpClient.BaseAddress = new Uri(baseUrl ?? "https://api.cohere.ai/v1/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Reranks the supplied documents against the query using the Cohere Rerank API.
    /// </summary>
    /// <param name="query">The query to score documents against.</param>
    /// <param name="documents">The candidate documents to rerank.</param>
    /// <param name="topK">The maximum number of results to return.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>Results ordered by descending relevance score, as returned by the API.</returns>
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (documents == null || documents.Count == 0)
        {
            return Array.Empty<RerankResult>();
        }

        var request = new
        {
            model = _model,
            query,
            documents = documents.ToArray(),
            top_n = Math.Min(topK, documents.Count),
            return_documents = false
        };

        _logger?.LogDebug(
            "Reranking {DocumentCount} documents with Cohere model {Model} (top_n={TopN}).",
            documents.Count, _model, request.top_n);

        var response = await _httpClient.PostAsJsonAsync("rerank", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CohereRerankResponse>(
            JsonOptions, cancellationToken);

        if (result?.Results == null)
        {
            return Array.Empty<RerankResult>();
        }

        var mapped = new List<RerankResult>(result.Results.Count);
        foreach (var item in result.Results)
        {
            mapped.Add(new RerankResult
            {
                Index = item.Index,
                Score = item.RelevanceScore,
                Document = item.Index >= 0 && item.Index < documents.Count
                    ? documents[item.Index]
                    : string.Empty
            });
        }

        return mapped;
    }

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/> if it was created by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region Private-Types

    private sealed class CohereRerankResponse
    {
        [JsonPropertyName("results")]
        public List<CohereRerankResult> Results { get; set; } = new();
    }

    private sealed class CohereRerankResult
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("relevance_score")]
        public double RelevanceScore { get; set; }
    }

    #endregion
}
