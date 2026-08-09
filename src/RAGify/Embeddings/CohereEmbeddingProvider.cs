using System.Net.Http.Json;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.Core;
using RAGify.Embeddings.Models;

namespace RAGify.Embeddings;

/// <summary>
/// Cohere embedding provider using the Cohere API.
/// Supports embed-english-v3.0, embed-multilingual-v3.0, etc.
/// </summary>
public class CohereEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _inputType;
    private readonly int _dimension;
    private readonly bool _disposeHttpClient;

    #endregion

    #region Public-Members

    /// <summary>
    /// Gets the dimension of vectors produced by this provider.
    /// </summary>
    public int Dimension => _dimension;

    #endregion

    /// <summary>
    /// Initializes a new instance of the CohereEmbeddingProvider.
    /// </summary>
    /// <param name="apiKey">Cohere API key.</param>
    /// <param name="model">Model name (e.g., "embed-english-v3.0", "embed-multilingual-v3.0").</param>
    /// <param name="inputType">Input type: "search_document", "search_query", "classification", or "clustering" (default: "search_document").</param>
    /// <param name="dimension">Vector dimension. For v3 models, can be 1024, 384, or 512.</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api.cohere.ai/v1).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    public CohereEmbeddingProvider(
        string apiKey,
        string model = "embed-english-v3.0",
        string inputType = "search_document",
        int? dimension = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _inputType = inputType ?? throw new ArgumentNullException(nameof(inputType));
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;

        _httpClient.BaseAddress = new Uri(baseUrl ?? "https://api.cohere.ai/v1");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");

        _dimension = dimension ?? GetDefaultDimensionForModel(model);
    }

    #region Public-Methods

    /// <summary>
    /// Generates an embedding vector for the specified text using Cohere's API.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the normalized embedding vector.</returns>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        try
        {
            return await EmbedAsyncInternal(text, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw;
        }
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts in batch using Cohere's API.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of normalized embedding vectors.</returns>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts == null || texts.Count == 0)
            return Array.Empty<float[]>();

        var request = new
        {
            texts = texts.ToArray(),
            model = _model,
            input_type = _inputType,
            embedding_types = new[] { "float" },
            truncate = "END"
        };

        var response = await _httpClient.PostAsJsonAsync("embed", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CohereEmbeddingResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (result?.Embeddings == null)
            throw new InvalidOperationException("No embedding data returned from Cohere API.");

        var embeddings = new List<float[]>();
        foreach (var embeddingList in result.Embeddings)
        {
            var embedding = embeddingList.ToArray();
            embeddings.Add(VectorMath.Normalize(embedding));
        }

        return embeddings;
    }

    /// <summary>
    /// Disposes the HttpClient if it was created by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient?.Dispose();
        }
    }

    #endregion

    #region Private-Methods

    private static int GetDefaultDimensionForModel(string model)
    {
        return model.ToLowerInvariant() switch
        {
            var m when m.Contains("v3.0") => 1024,
            var m when m.Contains("v2") => 4096,
            var m when m.Contains("v1") => 1024,
            _ => 1024
        };
    }

    /// <summary>
    /// Internal embedding method that handles context length errors by splitting.
    /// </summary>
    private async Task<float[]> EmbedAsyncInternal(string text, CancellationToken cancellationToken, int recursionDepth = 0)
    {
        if (recursionDepth > 5)
        {
            throw new InvalidOperationException(
                $"Text chunk is too large even after multiple splits. " +
                $"Original text length: {text.Length} characters. " +
                $"Please reduce the chunk size in your ChunkingOptions.");
        }

        var request = new
        {
            texts = new[] { text },
            model = _model,
            input_type = _inputType,
            embedding_types = new[] { "float" },
            truncate = "END"
        };

        var response = await _httpClient.PostAsJsonAsync("embed", request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Check for context length/token limit errors - split and retry
            if ((response.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                 response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge) &&
                (errorContent.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("maximum context length", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("too long", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("input too long", StringComparison.OrdinalIgnoreCase)))
            {
                return await EmbedWithSplittingAsync(text, cancellationToken, recursionDepth);
            }
            
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<CohereEmbeddingResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (result?.Embeddings == null || result.Embeddings.Count == 0)
            throw new InvalidOperationException("No embedding data returned from Cohere API.");

        var embedding = result.Embeddings[0].ToArray();
        return VectorMath.Normalize(embedding);
    }

    /// <summary>
    /// Splits text that exceeds context length into smaller chunks and averages their embeddings.
    /// </summary>
    private async Task<float[]> EmbedWithSplittingAsync(string text, CancellationToken cancellationToken, int recursionDepth = 0)
    {
        var maxChunkSize = Math.Max(100, text.Length / 2);
        var chunks = EmbeddingHelpers.SplitTextIntoChunks(text, maxChunkSize);
        
        if (chunks.Count == 0)
            throw new InvalidOperationException("Failed to split text into chunks.");

        var embeddings = new List<float[]>();
        
        foreach (var chunk in chunks)
        {
            try
            {
                var embedding = await EmbedAsyncInternal(chunk, cancellationToken, recursionDepth + 1);
                embeddings.Add(embedding);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                                                       ex.Message.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                                                       ex.Message.Contains("too long", StringComparison.OrdinalIgnoreCase))
            {
                var subEmbedding = await EmbedWithSplittingAsync(chunk, cancellationToken, recursionDepth + 1);
                embeddings.Add(subEmbedding);
            }
        }

        return EmbeddingHelpers.AverageEmbeddings(embeddings);
    }

    #endregion
}
