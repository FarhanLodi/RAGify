namespace RAGify.Abstractions;

/// <summary>
/// Provides functionality to split documents into chunks.
/// </summary>
public interface IChunkingStrategy
{
    #region Public-Methods

    /// <summary>
    /// Splits a document into chunks based on the strategy's rules.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks.</returns>
    Task<IReadOnlyList<IChunk>> ChunkAsync(IDocument document, CancellationToken cancellationToken = default);

    #endregion
}
