using System.Net.Http.Json;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.Core;
using RAGify.Embeddings.Models;

namespace RAGify.Embeddings;

/// <summary>
/// Azure OpenAI embedding provider using the Azure OpenAI API.
/// Supports text-embedding-ada-002, text-embedding-3-small, text-embedding-3-large, etc.
/// </summary>
public class AzureOpenAIEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _deploymentName;
    private readonly string _apiVersion;
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
    /// Initializes a new instance of the AzureOpenAIEmbeddingProvider.
    /// </summary>
    /// <param name="apiKey">Azure OpenAI API key.</param>
    /// <param name="deploymentName">The deployment name (e.g., "text-embedding-ada-002").</param>
    /// <param name="resourceName">Azure resource name (e.g., "my-resource").</param>
    /// <param name="apiVersion">API version (default: "2024-02-15-preview").</param>
    /// <param name="dimension">Vector dimension. For text-embedding-3 models, this can be customized.</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    public AzureOpenAIEmbeddingProvider(
        string apiKey,
        string deploymentName,
        string resourceName,
        string apiVersion = "2024-02-15-preview",
        int? dimension = null,
        HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _deploymentName = deploymentName ?? throw new ArgumentNullException(nameof(deploymentName));
        _apiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;

        var baseUrl = $"https://{resourceName}.openai.azure.com/openai/deployments/{_deploymentName}/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

        _dimension = dimension ?? GetDefaultDimensionForModel(deploymentName);
    }

    #region Public-Methods

    /// <summary>
    /// Generates an embedding vector for the specified text using Azure OpenAI's API.
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
    /// Generates embedding vectors for multiple texts in batch using Azure OpenAI's API.
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
            input = texts.ToArray(),
            dimensions = _dimension
        };

        var url = $"embeddings?api-version={_apiVersion}";
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (result?.Data == null)
            throw new InvalidOperationException("No embedding data returned from Azure OpenAI API.");

        var embeddings = new List<float[]>();
        foreach (var item in result.Data.OrderBy(d => d.Index))
        {
            var embedding = item.Embedding;
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
            input = text,
            dimensions = _dimension
        };

        var url = $"embeddings?api-version={_apiVersion}";
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        
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
            throw new InvalidOperationException("No embedding data returned from Azure OpenAI API.");

        var embedding = result.Data[0].Embedding;
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
