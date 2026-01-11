using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;
using RAGify.Embeddings.Models;

namespace RAGify.Embeddings;

/// <summary>
/// OpenAI embedding provider using the OpenAI API.
/// Supports text-embedding-ada-002, text-embedding-3-small, text-embedding-3-large, etc.
/// </summary>
public class OpenAIEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _dimension;
    private readonly bool _disposeHttpClient;
    private readonly ILogger<OpenAIEmbeddingProvider>? _logger;

    #endregion

    #region Public-Members

    /// <summary>
    /// Gets the dimension of vectors produced by this provider.
    /// </summary>
    public int Dimension => _dimension;

    #endregion

    /// <summary>
    /// Initializes a new instance of the OpenAIEmbeddingProvider.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Model name (e.g., "text-embedding-ada-002", "text-embedding-3-small").</param>
    /// <param name="dimension">Vector dimension. For text-embedding-3 models, this can be customized.</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api.openai.com/v1).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    public OpenAIEmbeddingProvider(
        string apiKey,
        string model = "text-embedding-ada-002",
        int? dimension = null,
        string? baseUrl = null,
        HttpClient? httpClient = null,
        ILogger<OpenAIEmbeddingProvider>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;
        _logger = logger;

        if (baseUrl != null)
        {
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
        else
        {
            _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        }

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        _dimension = dimension ?? GetDefaultDimensionForModel(model);
        _logger?.LogInformation("Initialized OpenAI embedding provider with model {Model} and dimension {Dimension}", _model, _dimension);
    }

    #region Public-Methods

    /// <summary>
    /// Generates an embedding vector for the specified text using OpenAI's API.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the normalized embedding vector.</returns>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        _logger?.LogDebug("Generating embedding for text of length {TextLength} using model {Model}", text.Length, _model);

        var request = new
        {
            model = _model,
            input = text,
            dimensions = _dimension
        };

        try
        {
            return await EmbedAsyncInternal(text, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error while generating embedding from OpenAI API");
            throw;
        }
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts in batch using OpenAI's API.
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

        _logger?.LogDebug("Generating batch embeddings for {TextCount} texts using model {Model}", texts.Count, _model);

        var request = new
        {
            model = _model,
            input = texts.ToArray(),
            dimensions = _dimension
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            if (result?.Data == null)
            {
                _logger?.LogError("No embedding data returned from OpenAI API for batch request");
                throw new InvalidOperationException("No embedding data returned from OpenAI API.");
            }

            var embeddings = new List<float[]>();
            foreach (var item in result.Data.OrderBy(d => d.Index))
            {
                var embedding = item.Embedding;
                embeddings.Add(VectorMath.Normalize(embedding));
            }

            _logger?.LogDebug("Successfully generated {EmbeddingCount} embeddings in batch", embeddings.Count);
            return embeddings;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error while generating batch embeddings from OpenAI API");
            throw;
        }
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
        return model switch
        {
            "text-embedding-ada-002" => 1536,
            "text-embedding-3-small" => 1536,
            "text-embedding-3-large" => 3072,
            _ => 1536
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
            model = _model,
            input = text,
            dimensions = _dimension
        };

        var response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Check for context length/token limit errors - split and retry
            if ((response.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                 response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge) &&
                (errorContent.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("maximum context length", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("too long", StringComparison.OrdinalIgnoreCase)))
            {
                return await EmbedWithSplittingAsync(text, cancellationToken, recursionDepth);
            }
            
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (result?.Data == null || result.Data.Count == 0)
        {
            _logger?.LogError("No embedding data returned from OpenAI API");
            throw new InvalidOperationException("No embedding data returned from OpenAI API.");
        }

        var embedding = result.Data[0].Embedding;
        _logger?.LogDebug("Successfully generated embedding with dimension {Dimension}", embedding.Length);
        
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
