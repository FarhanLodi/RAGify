using RAGify.Abstractions;

namespace RAGify.Embeddings;

/// <summary>
/// An <see cref="IEmbeddingProvider"/> decorator that splits large batch requests into smaller
/// consecutive sub-batches before delegating to an inner provider. This keeps individual API
/// requests within provider-imposed batch-size limits while preserving the original order.
/// </summary>
public class BatchingEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    #region Private-Members

    private readonly IEmbeddingProvider _inner;
    private readonly int _maxBatchSize;

    #endregion

    #region Public-Members

    /// <summary>
    /// Gets the dimension of vectors produced by the inner provider.
    /// </summary>
    public int Dimension => _inner.Dimension;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchingEmbeddingProvider"/> class.
    /// </summary>
    /// <param name="inner">The underlying embedding provider that performs the work.</param>
    /// <param name="maxBatchSize">The maximum number of texts to send per inner batch request. Must be greater than zero.</param>
    public BatchingEmbeddingProvider(IEmbeddingProvider inner, int maxBatchSize = 96)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        if (maxBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize), "maxBatchSize must be greater than zero.");

        _maxBatchSize = maxBatchSize;
    }

    #region Public-Methods

    /// <summary>
    /// Generates an embedding for the specified text by delegating to the inner provider.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the embedding vector.</returns>
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return _inner.EmbedAsync(text, cancellationToken);
    }

    /// <summary>
    /// Generates embeddings for multiple texts, splitting the input into consecutive sub-batches
    /// of at most the configured maximum batch size and concatenating the results in original order.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of embedding vectors in the original order.</returns>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts == null || texts.Count == 0)
            return Array.Empty<float[]>();

        var results = new List<float[]>(texts.Count);

        for (int offset = 0; offset < texts.Count; offset += _maxBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(_maxBatchSize, texts.Count - offset);
            var subBatch = new List<string>(count);
            for (int i = 0; i < count; i++)
                subBatch.Add(texts[offset + i]);

            var embeddings = await _inner.EmbedBatchAsync(subBatch, cancellationToken).ConfigureAwait(false);
            results.AddRange(embeddings);
        }

        return results;
    }

    /// <summary>
    /// Disposes the inner provider if it implements <see cref="IDisposable"/>.
    /// </summary>
    public void Dispose()
    {
        if (_inner is IDisposable d)
            d.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion
}
