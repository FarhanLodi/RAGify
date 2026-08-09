using System.Net.Http.Json;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Embeddings;

/// <summary>
/// Hugging Face Inference API embedding provider.
/// Supports any embedding model available on Hugging Face.
/// </summary>
public class HuggingFaceEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelId;
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
    /// Initializes a new instance of the HuggingFaceEmbeddingProvider.
    /// </summary>
    /// <param name="apiKey">Hugging Face API key (optional for public models).</param>
    /// <param name="modelId">Model ID (e.g., "sentence-transformers/all-MiniLM-L6-v2").</param>
    /// <param name="dimension">Vector dimension. If not specified, will be determined from the model.</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api-inference.huggingface.co).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    public HuggingFaceEmbeddingProvider(
        string? apiKey,
        string modelId,
        int? dimension = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? string.Empty;
        _modelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;

        _httpClient.BaseAddress = new Uri(baseUrl ?? "https://api-inference.huggingface.co");
        
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        _dimension = dimension ?? GetDefaultDimensionForModel(modelId);
    }

    #region Public-Methods

    /// <summary>
    /// Generates an embedding vector for the specified text using Hugging Face's Inference API.
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
    /// Generates embedding vectors for multiple texts in batch using Hugging Face's Inference API.
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
            inputs = texts.ToArray()
        };

        var url = $"/pipeline/feature-extraction/{_modelId}";
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        
        if (result.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Invalid response format from Hugging Face API.");

        var embeddings = new List<float[]>();
        
        foreach (var item in result.EnumerateArray())
        {
            var embedding = new List<float>();
            foreach (var element in item.EnumerateArray())
            {
                embedding.Add(element.GetSingle());
            }
            embeddings.Add(VectorMath.Normalize(embedding.ToArray()));
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

    private static int GetDefaultDimensionForModel(string modelId)
    {
        return modelId.ToLowerInvariant() switch
        {
            var m when m.Contains("all-minilm-l6-v2") => 384,
            var m when m.Contains("all-mpnet-base-v2") => 768,
            var m when m.Contains("all-distilroberta-v1") => 768,
            var m when m.Contains("paraphrase-multilingual") => 768,
            _ => 384
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
            inputs = text
        };

        var url = $"/pipeline/feature-extraction/{_modelId}";
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Check for context length/token limit errors - split and retry
            if ((response.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                 response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge ||
                 response.StatusCode == System.Net.HttpStatusCode.InternalServerError) &&
                (errorContent.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("maximum context length", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("too long", StringComparison.OrdinalIgnoreCase) ||
                 errorContent.Contains("Input too long", StringComparison.OrdinalIgnoreCase)))
            {
                return await EmbedWithSplittingAsync(text, cancellationToken, recursionDepth);
            }
            
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            throw new InvalidOperationException("Invalid response format from Hugging Face API.");

        var embeddingArray = result[0];
        var embedding = new List<float>();
        
        foreach (var element in embeddingArray.EnumerateArray())
        {
            embedding.Add(element.GetSingle());
        }

        var embeddingArray_final = embedding.ToArray();
        return VectorMath.Normalize(embeddingArray_final);
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
