using System.Net.Http.Json;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.Core;
using RAGify.Embeddings.Models;

namespace RAGify.Embeddings;

/// <summary>
/// Ollama embedding provider for local or remote Ollama instances.
/// Supports models like nomic-embed-text, all-minilm, etc.
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _model;
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
    /// Initializes a new instance of the OllamaEmbeddingProvider.
    /// </summary>
    /// <param name="model">Model name (e.g., "nomic-embed-text", "all-minilm").</param>
    /// <param name="baseUrl">Base URL for Ollama API (default: http://localhost:11434).</param>
    /// <param name="dimension">Vector dimension. If not specified, will be determined from the model or API.</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    public OllamaEmbeddingProvider(
        string model = "nomic-embed-text",
        string? baseUrl = null,
        int? dimension = null,
        HttpClient? httpClient = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;

        var url = baseUrl ?? "http://localhost:11434";
        if (!url.EndsWith("/"))
            url += "/";
        
        _httpClient.BaseAddress = new Uri(url);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);

        _dimension = dimension ?? GetDefaultDimensionForModel(model);
    }

    #region Public-Methods

    /// <summary>
    /// Generates an embedding vector for the specified text using Ollama's API.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the normalized embedding vector.</returns>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        var request = new
        {
            model = _model,
            prompt = text
        };

        try
        {
            return await EmbedAsyncInternal(text, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to Ollama. Make sure Ollama is running and the model '{_model}' is available. " +
                $"You can pull the model with: ollama pull {_model}. " +
                $"Error: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new InvalidOperationException(
                $"Request to Ollama timed out. Make sure Ollama is running and the model '{_model}' is available. " +
                $"You can pull the model with: ollama pull {_model}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse response from Ollama API. Make sure Ollama is running correctly and the model '{_model}' is available. " +
                $"You can pull the model with: ollama pull {_model}. Error: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Failed to get embedding from Ollama. Make sure Ollama is running and the model '{_model}' is available. " +
                $"You can pull the model with: ollama pull {_model}. Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts in batch using Ollama's API.
    /// Note: Ollama doesn't support batch embeddings natively, so they are processed sequentially.
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

        var embeddings = new List<float[]>();
        
        foreach (var text in texts)
        {
            var embedding = await EmbedAsync(text, cancellationToken);
            embeddings.Add(embedding);
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
            "nomic-embed-text" => 768,
            "all-minilm" => 384,
            "mxbai-embed-large" => 1024,
            _ => 768
        };
    }

    /// <summary>
    /// Splits text that exceeds context length into smaller chunks and averages their embeddings.
    /// </summary>
    private async Task<float[]> EmbedWithSplittingAsync(string text, CancellationToken cancellationToken, int recursionDepth = 0)
    {
        // Prevent infinite recursion - if we've split too many times, throw an error
        if (recursionDepth > 5)
        {
            throw new InvalidOperationException(
                $"Text chunk is too large even after multiple splits. " +
                $"Original text length: {text.Length} characters. " +
                $"Please reduce the chunk size in your ChunkingOptions.");
        }

        // Estimate max chunk size (use 50% of original to be safe, split into at least 2 chunks)
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
            catch (InvalidOperationException ex) when (ex.Message.Contains("context length", StringComparison.OrdinalIgnoreCase))
            {
                // If this chunk is still too large, recursively split it further
                var subEmbedding = await EmbedWithSplittingAsync(chunk, cancellationToken, recursionDepth + 1);
                embeddings.Add(subEmbedding);
            }
        }

        // Average all embeddings to get a single embedding vector
        return EmbeddingHelpers.AverageEmbeddings(embeddings);
    }

    /// <summary>
    /// Internal embedding method that handles context length errors by splitting.
    /// </summary>
    private async Task<float[]> EmbedAsyncInternal(string text, CancellationToken cancellationToken, int recursionDepth = 0)
    {
        var request = new
        {
            model = _model,
            prompt = text
        };

        var response = await _httpClient.PostAsJsonAsync("api/embeddings", request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Check for context length error specifically - split and retry
            if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError && 
                errorContent.Contains("context length", StringComparison.OrdinalIgnoreCase))
            {
                // Split into smaller chunks and average the embeddings
                return await EmbedWithSplittingAsync(text, cancellationToken, recursionDepth);
            }
            
            throw new InvalidOperationException(
                $"Ollama API error: {errorContent}. " +
                $"Make sure Ollama is running and the model '{_model}' is available. " +
                $"You can pull the model with: ollama pull {_model}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (result?.Embedding == null || result.Embedding.Length == 0)
            throw new InvalidOperationException("No embedding data returned from Ollama API.");

        return VectorMath.Normalize(result.Embedding);
    }


    #endregion
}
