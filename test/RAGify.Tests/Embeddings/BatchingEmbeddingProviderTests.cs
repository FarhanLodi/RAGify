using RAGify.Abstractions;
using RAGify.Embeddings;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="BatchingEmbeddingProvider"/>.
/// </summary>
public class BatchingEmbeddingProviderTests
{
    #region Test-Doubles

    /// <summary>
    /// An <see cref="IEmbeddingProvider"/> that records the size of each batch it receives and
    /// returns deterministic vectors so result order/length can be asserted.
    /// </summary>
    private sealed class CountingProvider : IEmbeddingProvider
    {
        public List<int> BatchSizes { get; } = new();

        public int Dimension => 1;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VectorFor(text));
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            BatchSizes.Add(texts.Count);
            IReadOnlyList<float[]> results = texts.Select(VectorFor).ToArray();
            return Task.FromResult(results);
        }

        private static float[] VectorFor(string text) => new float[] { int.Parse(text) };
    }

    #endregion

    #region Tests

    [Fact]
    public async Task EmbedBatchAsync_SplitsIntoSubBatches_AndPreservesOrder()
    {
        var inner = new CountingProvider();
        var provider = new BatchingEmbeddingProvider(inner, maxBatchSize: 2);

        var texts = new[] { "0", "1", "2", "3", "4" };
        var results = await provider.EmbedBatchAsync(texts);

        // 5 texts with maxBatchSize 2 => sub-batches of 2, 2, 1.
        Assert.Equal(new[] { 2, 2, 1 }, inner.BatchSizes);

        // Result length and order preserved.
        Assert.Equal(5, results.Count);
        for (int i = 0; i < texts.Count(); i++)
            Assert.Equal((float)i, results[i][0]);
    }

    [Fact]
    public async Task EmbedBatchAsync_EmptyInput_ReturnsEmpty()
    {
        var inner = new CountingProvider();
        var provider = new BatchingEmbeddingProvider(inner, maxBatchSize: 2);

        var results = await provider.EmbedBatchAsync(Array.Empty<string>());

        Assert.Empty(results);
        Assert.Empty(inner.BatchSizes);
    }

    [Fact]
    public void Ctor_InvalidBatchSize_Throws()
    {
        var inner = new CountingProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => new BatchingEmbeddingProvider(inner, maxBatchSize: 0));
    }

    #endregion
}
