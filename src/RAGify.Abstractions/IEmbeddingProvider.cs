namespace RAGify.Abstractions;

/// <summary>
/// Provides functionality to generate embeddings from text.
/// </summary>
public interface IEmbeddingProvider
{
    #region Public-Members

    /// <summary>
    /// Gets the dimension of vectors produced by this provider.
    /// </summary>
    int Dimension { get; }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Generates an embedding vector for the specified text.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the embedding vector.</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in batch.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of embedding vectors.</returns>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

    #endregion
}
