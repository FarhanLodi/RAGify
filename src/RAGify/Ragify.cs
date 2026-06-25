using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Chunking;
using RAGify.Core;
using RAGify.Embeddings;
using RAGify.Generation;
using RAGify.Ingestion;
using RAGify.Retrieval;
using RAGify.VectorStores;

namespace RAGify;

/// <summary>
/// Orchestrates the RAG pipeline including document ingestion, chunking, embedding, and retrieval.
/// </summary>
public class Ragify : IRagify
{
    #region Private-Members

    private readonly IChunkingStrategy _chunkingStrategy;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IVectorStore _vectorStore;
    private readonly IRetrievalEngine _retrievalEngine;
    private readonly TextCleanupOptions _textCleanupOptions;
    private readonly ILogger<Ragify>? _logger;
    private readonly ILlmProvider? _llmProvider;
    private readonly GenerationOptions _generationOptions;
    private readonly RagPromptBuilder _promptBuilder = new();
    private readonly Dictionary<string, IDocument> _documents = new();
    private readonly Dictionary<string, IChunk> _chunks = new();

    #endregion

    public Ragify(
        IChunkingStrategy chunkingStrategy,
        IEmbeddingProvider embeddingProvider,
        IVectorStore vectorStore,
        IRetrievalEngine retrievalEngine,
        TextCleanupOptions? textCleanupOptions = null,
        ILogger<Ragify>? logger = null,
        ILlmProvider? llmProvider = null,
        GenerationOptions? generationOptions = null)
    {
        _chunkingStrategy = chunkingStrategy ?? throw new ArgumentNullException(nameof(chunkingStrategy));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _retrievalEngine = retrievalEngine ?? throw new ArgumentNullException(nameof(retrievalEngine));
        _textCleanupOptions = textCleanupOptions ?? new TextCleanupOptions();
        _logger = logger;
        _llmProvider = llmProvider;
        _generationOptions = generationOptions ?? new GenerationOptions();
    }

    #region Public-Methods

    /// <summary>
    /// Ingests a document into the RAG system by chunking, embedding, and storing it.
    /// </summary>
    /// <param name="document">The document to ingest.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task IngestAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Starting ingestion for document {DocumentId} from source {Source}", document.DocumentId, document.Source);

        _documents[document.DocumentId] = document;

        var documentToChunk = document;
        if (_textCleanupOptions.Enabled)
        {
            _logger?.LogDebug("Text cleanup enabled, cleaning document content");
            var cleanedContent = TextCleanupService.CleanText(document.Content, _textCleanupOptions);
            documentToChunk = new Document
            {
                DocumentId = document.DocumentId,
                Content = cleanedContent,
                Source = document.Source,
                Page = document.Page,
                Metadata = document.Metadata
            };
        }

        _logger?.LogDebug("Chunking document {DocumentId}", document.DocumentId);
        var chunks = await _chunkingStrategy.ChunkAsync(documentToChunk, cancellationToken);
        _logger?.LogInformation("Document {DocumentId} split into {ChunkCount} chunks", document.DocumentId, chunks.Count);

        var texts = chunks.Select(c => c.Text).ToList();
        _logger?.LogDebug("Generating embeddings for {ChunkCount} chunks", chunks.Count);
        var embeddings = await _embeddingProvider.EmbedBatchAsync(texts, cancellationToken);
        _logger?.LogDebug("Generated {EmbeddingCount} embeddings", embeddings.Count);

        var vectorData = new List<VectorData>();
        foreach (var (chunk, embedding) in chunks.Zip(embeddings))
        {
            _chunks[chunk.ChunkId] = chunk;

            var metadata = new Dictionary<string, object>(chunk.Metadata)
            {
                ["ChunkId"] = chunk.ChunkId,
                ["DocumentId"] = chunk.DocumentId,
                ["Index"] = chunk.Index,
                ["Text"] = chunk.Text
            };

            vectorData.Add(new VectorData
            {
                VectorId = chunk.ChunkId,
                Vector = embedding,
                Metadata = metadata
            });
        }

        _logger?.LogDebug("Upserting {VectorCount} vectors to vector store", vectorData.Count);
        await _vectorStore.UpsertBatchAsync(vectorData, cancellationToken);

        if (_retrievalEngine is Retrieval.RetrievalEngine retrievalEngine)
        {
            retrievalEngine.RegisterChunks(chunks);
        }

        _logger?.LogInformation("Successfully ingested document {DocumentId} with {ChunkCount} chunks", document.DocumentId, chunks.Count);
    }

    /// <summary>
    /// Ingests multiple documents into the RAG system.
    /// </summary>
    /// <param name="documents">The documents to ingest.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task IngestBatchAsync(IReadOnlyList<IDocument> documents, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Starting batch ingestion for {DocumentCount} documents", documents.Count);
        foreach (var document in documents)
        {
            await IngestAsync(document, cancellationToken);
        }
        _logger?.LogInformation("Completed batch ingestion for {DocumentCount} documents", documents.Count);
    }

    /// <summary>
    /// Queries the RAG system to retrieve relevant chunks for the given query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="options">Optional query options for customizing retrieval behavior.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the query results with relevant chunks.</returns>
    public async Task<QueryResult> QueryAsync(
        string query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Processing query: {Query}", query);
        options ??= new QueryOptions();

        IReadOnlyList<RetrievalResult> retrievalResults;
        RetrievalMetadata? metadata = null;

        if (_retrievalEngine is Retrieval.RetrievalEngine retrievalEngine)
        {
            var (results, meta) = await retrievalEngine.RetrieveWithMetadataAsync(query, options.Retrieval, cancellationToken);
            retrievalResults = results;
            metadata = meta;
        }
        else
        {
            retrievalResults = await _retrievalEngine.RetrieveAsync(query, options.Retrieval, cancellationToken);
        }

        _logger?.LogDebug("Retrieved {ResultCount} results for query", retrievalResults.Count);

        foreach (var result in retrievalResults)
        {
            var chunk = result.Chunk;
            
            if (_documents.TryGetValue(chunk.DocumentId, out var document))
            {
                result.Source = document.Source;
                result.Page = document.Page;
            }
        }

        _logger?.LogInformation("Query completed with {ResultCount} results", retrievalResults.Count);
        return new QueryResult
        {
            Context = retrievalResults,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Retrieves relevant context and generates a grounded natural-language answer for the query.
    /// </summary>
    /// <param name="query">The question to answer.</param>
    /// <param name="options">Optional query options for customizing retrieval and generation behavior.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task whose result contains the generated answer along with the retrieved context.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no LLM provider has been configured.</exception>
    public async Task<QueryResult> AnswerAsync(
        string query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureLlmConfigured();

        var queryResult = await QueryAsync(query, options, cancellationToken);
        var generationOptions = options?.Generation ?? _generationOptions;
        var messages = _promptBuilder.Build(query, queryResult.Context, generationOptions);
        var chatOptions = new ChatOptions
        {
            Temperature = generationOptions.Temperature,
            MaxTokens = generationOptions.MaxTokens
        };

        _logger?.LogInformation("Generating answer using {ContextCount} retrieved chunks", queryResult.Context.Count);
        var completion = await _llmProvider!.CompleteAsync(messages, chatOptions, cancellationToken);

        queryResult.Answer = completion.Content;
        queryResult.Generation = new GenerationMetadata
        {
            Model = completion.Model,
            PromptTokens = completion.PromptTokens,
            CompletionTokens = completion.CompletionTokens,
            FinishReason = completion.FinishReason
        };

        _logger?.LogInformation("Answer generated ({CompletionTokens} completion tokens)", completion.CompletionTokens);
        return queryResult;
    }

    /// <summary>
    /// Retrieves relevant context and streams a grounded natural-language answer token-by-token.
    /// </summary>
    /// <param name="query">The question to answer.</param>
    /// <param name="options">Optional query options for customizing retrieval and generation behavior.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An asynchronous stream of incremental answer fragments.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no LLM provider has been configured.</exception>
    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string query,
        QueryOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureLlmConfigured();

        var queryResult = await QueryAsync(query, options, cancellationToken);
        var generationOptions = options?.Generation ?? _generationOptions;
        var messages = _promptBuilder.Build(query, queryResult.Context, generationOptions);
        var chatOptions = new ChatOptions
        {
            Temperature = generationOptions.Temperature,
            MaxTokens = generationOptions.MaxTokens
        };

        _logger?.LogInformation("Streaming answer using {ContextCount} retrieved chunks", queryResult.Context.Count);
        await foreach (var fragment in _llmProvider!.StreamAsync(messages, chatOptions, cancellationToken))
        {
            yield return fragment;
        }
    }

    /// <summary>
    /// Gets a list of all document IDs that have been indexed.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of document IDs.</returns>
    public Task<IReadOnlyList<string>> GetIndexedDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(_documents.Keys.ToList());
    }

    /// <summary>
    /// Gets all chunks for a specific document.
    /// </summary>
    /// <param name="documentId">The document ID to get chunks for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks ordered by index.</returns>
    public Task<IReadOnlyList<IChunk>> GetChunksAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var chunks = _chunks.Values
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.Index)
            .ToList();

        return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
    }

    /// <summary>
    /// Clears all ingested documents and chunks from the RAG system.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Clearing all documents and chunks from the RAG system");
        _documents.Clear();
        _chunks.Clear();

        // Clear the retrieval engine's chunk cache so stale chunks are not returned after a reset.
        if (_retrievalEngine is Retrieval.RetrievalEngine retrievalEngine)
        {
            retrievalEngine.ClearCache();
        }

        await _vectorStore.ClearAsync(cancellationToken);
        _logger?.LogInformation("Successfully cleared all data from the RAG system");
    }

    #endregion

    #region Private-Methods

    private void EnsureLlmConfigured()
    {
        if (_llmProvider == null)
        {
            throw new InvalidOperationException(
                "No LLM provider is configured. Call one of the chat-provider builder methods " +
                "(WithOpenAIChat(), WithAzureOpenAIChat(), WithAnthropicChat(), WithOllamaChat()) " +
                "or WithLlm() before using AnswerAsync()/StreamAnswerAsync().");
        }
    }

    #endregion
}
