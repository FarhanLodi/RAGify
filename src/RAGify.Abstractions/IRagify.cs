namespace RAGify.Abstractions;

/// <summary>
/// Main interface for the RAG pipeline including document ingestion, chunking, embedding, and retrieval.
/// </summary>
public interface IRagify
{
    #region Public-Methods

    /// <summary>
    /// Ingests a document into the RAG system by chunking, embedding, and storing it.
    /// </summary>
    /// <param name="document">The document to ingest.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task IngestAsync(IDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests multiple documents into the RAG system.
    /// </summary>
    /// <param name="documents">The documents to ingest.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task IngestBatchAsync(IReadOnlyList<IDocument> documents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the RAG system to retrieve relevant chunks for the given query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="options">Optional query options for customizing retrieval behavior.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the query results with relevant chunks.</returns>
    Task<QueryResult> QueryAsync(string query, QueryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of all document IDs that have been indexed.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of document IDs.</returns>
    Task<IReadOnlyList<string>> GetIndexedDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all chunks for a specific document.
    /// </summary>
    /// <param name="documentId">The document ID to get chunks for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks ordered by index.</returns>
    Task<IReadOnlyList<IChunk>> GetChunksAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all ingested documents and chunks from the RAG system.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    #endregion
}
