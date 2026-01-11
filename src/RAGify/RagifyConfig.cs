using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Chunking;
using RAGify.Core;
using RAGify.Embeddings;
using RAGify.Ingestion;
using RAGify.Retrieval;
using RAGify.VectorStores;
using Microsoft.ML.OnnxRuntime;

namespace RAGify;

/// <summary>
/// Fluent configuration API for setting up and creating a RAG orchestrator.
/// </summary>
public class RagifyConfig
{
    #region Private-Members

    private IChunkingStrategy? _chunkingStrategy;
    private IEmbeddingProvider? _embeddingProvider;
    private IVectorStore? _vectorStore;
    private IRetrievalEngine? _retrievalEngine;
    private ChunkingOptions _chunkingOptions = new();
    private TextCleanupOptions _textCleanupOptions = new();
    private List<IDocumentExtractor> _extractors = new();
    private ILogger<Ragify>? _logger;

    #endregion

    #region Public-Methods

    /// <summary>
    /// Configures the chunking strategy and options.
    /// </summary>
    /// <param name="strategy">The chunking strategy type to use.</param>
    /// <param name="options">Optional chunking options. If not provided, default options will be used.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithChunking(ChunkingStrategyType strategy, ChunkingOptions? options = null)
    {
        _chunkingOptions = options ?? new ChunkingOptions();
        
        // Note: Loggers for chunking strategies can be passed via dependency injection
        // For now, we pass null as loggers are optional
        _chunkingStrategy = strategy switch
        {
            ChunkingStrategyType.FixedSize => new FixedSizeChunkingStrategy(_chunkingOptions, null),
            ChunkingStrategyType.SentenceAware => new SentenceAwareChunkingStrategy(_chunkingOptions, null),
            ChunkingStrategyType.SlidingWindow => new SlidingWindowChunkingStrategy(_chunkingOptions, null),
            _ => throw new ArgumentException($"Unknown chunking strategy: {strategy}")
        };

        return this;
    }

    /// <summary>
    /// Configures text cleanup options.
    /// </summary>
    /// <param name="options">Optional text cleanup options. If not provided, default options will be used.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithTextCleanup(TextCleanupOptions? options = null)
    {
        _textCleanupOptions = options ?? new TextCleanupOptions();
        return this;
    }

    /// <summary>
    /// Configures a custom embedding provider.
    /// </summary>
    /// <param name="provider">The embedding provider to use.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithEmbeddings(IEmbeddingProvider provider)
    {
        _embeddingProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    /// <summary>
    /// Configures OpenAI embeddings.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Model name (default: "text-embedding-ada-002").</param>
    /// <param name="dimension">Vector dimension. For text-embedding-3 models, this can be customized.</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api.openai.com/v1).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithOpenAIEmbeddings(
        string apiKey,
        string model = "text-embedding-ada-002",
        int? dimension = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _embeddingProvider = new OpenAIEmbeddingProvider(apiKey, model, dimension, baseUrl, httpClient);
        return this;
    }

    /// <summary>
    /// Configures Ollama embeddings for local or remote Ollama instances.
    /// </summary>
    /// <param name="model">Model name (default: "nomic-embed-text").</param>
    /// <param name="baseUrl">Base URL for Ollama API (default: http://localhost:11434).</param>
    /// <param name="dimension">Vector dimension. If not specified, will be determined from the model.</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithOllamaEmbeddings(
        string model = "nomic-embed-text",
        string? baseUrl = null,
        int? dimension = null,
        HttpClient? httpClient = null)
    {
        _embeddingProvider = new OllamaEmbeddingProvider(model, baseUrl, dimension, httpClient);
        return this;
    }

    /// <summary>
    /// Configures Azure OpenAI embeddings.
    /// </summary>
    /// <param name="apiKey">Azure OpenAI API key.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="resourceName">Azure resource name.</param>
    /// <param name="apiVersion">API version (default: "2024-02-15-preview").</param>
    /// <param name="dimension">Vector dimension. For text-embedding-3 models, this can be customized.</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithAzureOpenAIEmbeddings(
        string apiKey,
        string deploymentName,
        string resourceName,
        string apiVersion = "2024-02-15-preview",
        int? dimension = null,
        HttpClient? httpClient = null)
    {
        _embeddingProvider = new AzureOpenAIEmbeddingProvider(apiKey, deploymentName, resourceName, apiVersion, dimension, httpClient);
        return this;
    }

    /// <summary>
    /// Configures ONNX embeddings for local ONNX models.
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model file.</param>
    /// <param name="dimension">Vector dimension. If not specified, will be inferred from model output.</param>
    /// <param name="inputName">Name of the input tensor (default: "input_ids").</param>
    /// <param name="outputName">Name of the output tensor (default: "last_hidden_state" or "embeddings").</param>
    /// <param name="tokenizer">Optional tokenizer function. If not provided, text will be split by whitespace.</param>
    /// <param name="sessionOptions">Optional ONNX Runtime session options.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithOnnxEmbeddings(
        string modelPath,
        int? dimension = null,
        string? inputName = null,
        string? outputName = null,
        Func<string, int[]>? tokenizer = null,
        SessionOptions? sessionOptions = null)
    {
        _embeddingProvider = new OnnxEmbeddingProvider(modelPath, dimension, inputName, outputName, tokenizer, sessionOptions);
        return this;
    }

    /// <summary>
    /// Configures Hugging Face Inference API embeddings.
    /// </summary>
    /// <param name="apiKey">Hugging Face API key (optional for public models).</param>
    /// <param name="modelId">Model ID (e.g., "sentence-transformers/all-MiniLM-L6-v2").</param>
    /// <param name="dimension">Vector dimension. If not specified, will be determined from the model.</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api-inference.huggingface.co).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithHuggingFaceEmbeddings(
        string? apiKey,
        string modelId,
        int? dimension = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _embeddingProvider = new HuggingFaceEmbeddingProvider(apiKey, modelId, dimension, baseUrl, httpClient);
        return this;
    }

    /// <summary>
    /// Configures Cohere embeddings.
    /// </summary>
    /// <param name="apiKey">Cohere API key.</param>
    /// <param name="model">Model name (default: "embed-english-v3.0").</param>
    /// <param name="inputType">Input type: "search_document", "search_query", "classification", or "clustering" (default: "search_document").</param>
    /// <param name="dimension">Vector dimension. For v3 models, can be 1024, 384, or 512.</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api.cohere.ai/v1).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithCohereEmbeddings(
        string apiKey,
        string model = "embed-english-v3.0",
        string inputType = "search_document",
        int? dimension = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _embeddingProvider = new CohereEmbeddingProvider(apiKey, model, inputType, dimension, baseUrl, httpClient);
        return this;
    }

    /// <summary>
    /// Configures VoyageAI embeddings.
    /// </summary>
    /// <param name="apiKey">VoyageAI API key.</param>
    /// <param name="model">Model name (default: "voyage-large-2").</param>
    /// <param name="dimension">Vector dimension. If not specified, will be determined from the model.</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api.voyageai.com/v1).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithVoyageAIEmbeddings(
        string apiKey,
        string model = "voyage-large-2",
        int? dimension = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _embeddingProvider = new VoyageAIEmbeddingProvider(apiKey, model, dimension, baseUrl, httpClient);
        return this;
    }

    /// <summary>
    /// Configures Google Gemini embeddings.
    /// </summary>
    /// <param name="apiKey">Google AI API key.</param>
    /// <param name="model">Model name (default: "text-embedding-004").</param>
    /// <param name="dimension">Vector dimension. If not specified, will be determined from the model.</param>
    /// <param name="baseUrl">Base URL for the API (default: https://generativelanguage.googleapis.com/v1beta).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithGoogleGeminiEmbeddings(
        string apiKey,
        string model = "text-embedding-004",
        int? dimension = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _embeddingProvider = new GoogleGeminiEmbeddingProvider(apiKey, model, dimension, baseUrl, httpClient);
        return this;
    }

    /// <summary>
    /// Configures a custom vector store.
    /// </summary>
    /// <param name="vectorStore">The vector store to use.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithVectorStore(IVectorStore vectorStore)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        return this;
    }

    /// <summary>
    /// Configures an in-memory vector store (useful for development and testing).
    /// </summary>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithInMemoryVectorStore()
    {
        // Note: Logger can be passed via dependency injection if needed
        _vectorStore = new InMemoryVectorStore(null);
        return this;
    }

    /// <summary>
    /// Adds a document extractor to the ingestion service.
    /// </summary>
    /// <param name="extractor">The document extractor to add.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig AddExtractor(IDocumentExtractor extractor)
    {
        if (extractor != null)
            _extractors.Add(extractor);
        return this;
    }

    /// <summary>
    /// Adds default document extractors (PDF, Word, Excel, HTML, PlainText).
    /// </summary>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithDefaultExtractors()
    {
        _extractors.Add(new PdfExtractor());
        _extractors.Add(new WordExtractor());
        _extractors.Add(new ExcelExtractor());
        _extractors.Add(new HtmlExtractor());
        _extractors.Add(new PlainTextExtractor());
        return this;
    }

    /// <summary>
    /// Configures logging for the RAG orchestrator.
    /// </summary>
    /// <param name="logger">The logger instance to use.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithLogger(ILogger<Ragify>? logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Builds and returns a configured RAG orchestrator instance.
    /// </summary>
    /// <returns>A configured IRagify instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required components (chunking strategy, embedding provider, or vector store) are not configured.</exception>
    public IRagify Build()
    {
        if (_chunkingStrategy == null)
            throw new InvalidOperationException("Chunking strategy must be configured. Call WithChunking().");

        if (_embeddingProvider == null)
            throw new InvalidOperationException("Embedding provider must be configured. Call WithEmbeddings() or one of the provider-specific methods (WithOpenAIEmbeddings(), WithOllamaEmbeddings(), WithAzureOpenAIEmbeddings(), etc.).");

        if (_vectorStore == null)
            throw new InvalidOperationException("Vector store must be configured. Call WithVectorStore() or WithInMemoryVectorStore().");

        _retrievalEngine ??= new RetrievalEngine(_embeddingProvider, _vectorStore, null);

        return new Ragify(
            _chunkingStrategy,
            _embeddingProvider,
            _vectorStore,
            _retrievalEngine,
            _textCleanupOptions,
            _logger);
    }

    #endregion
}
