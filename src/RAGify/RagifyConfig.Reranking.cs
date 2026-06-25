using RAGify.Abstractions;
using RAGify.Reranking;

namespace RAGify;

/// <summary>
/// Reranking configuration extensions for <see cref="RagifyConfig"/>.
/// </summary>
public partial class RagifyConfig
{
    #region Public-Methods

    /// <summary>
    /// Configures a custom reranker.
    /// </summary>
    /// <param name="reranker">The reranker to use.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithReranker(IReranker reranker)
    {
        _reranker = reranker ?? throw new ArgumentNullException(nameof(reranker));
        return this;
    }

    /// <summary>
    /// Configures a Cohere-backed reranker.
    /// </summary>
    /// <param name="apiKey">Cohere API key.</param>
    /// <param name="model">Rerank model name (default: "rerank-english-v3.0").</param>
    /// <param name="baseUrl">Optional base URL for the API.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, one is created internally.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithCohereReranker(
        string apiKey,
        string model = "rerank-english-v3.0",
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _reranker = new CohereReranker(apiKey, model, baseUrl, httpClient);
        return this;
    }

    /// <summary>
    /// Configures the dependency-free BM25 lexical reranker.
    /// </summary>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithLexicalReranker()
    {
        _reranker = new LexicalReranker();
        return this;
    }

    #endregion
}
