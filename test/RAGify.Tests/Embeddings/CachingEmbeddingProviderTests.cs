using RAGify.Abstractions;
using RAGify.Embeddings;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="CachingEmbeddingProvider"/>.
/// </summary>
public class CachingEmbeddingProviderTests
{
    #region Test-Doubles

    /// <summary>
    /// An <see cref="IEmbeddingProvider"/> that returns deterministic vectors and records how many
    /// texts it was asked to embed (across both single and batch calls).
    /// </summary>
    private sealed class CountingProvider : IEmbeddingProvider
    {
        public int EmbeddedCount { get; private set; }

        public int Dimension => 2;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            EmbeddedCount++;
            return Task.FromResult(VectorFor(text));
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            EmbeddedCount += texts.Count;
            IReadOnlyList<float[]> results = texts.Select(VectorFor).ToArray();
            return Task.FromResult(results);
        }

        private static float[] VectorFor(string text) => new float[] { text.Length, text.GetHashCode() & 0xFF };
    }

    #endregion

    #region Tests

    [Fact]
    public async Task EmbedAsync_SecondCall_HitsCache_DoesNotReembed()
    {
        var inner = new CountingProvider();
        var provider = new CachingEmbeddingProvider(inner, new InMemoryEmbeddingCache());

        var first = await provider.EmbedAsync("hello");
        var second = await provider.EmbedAsync("hello");

        Assert.Equal(1, inner.EmbeddedCount);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task EmbedBatchAsync_OnlyEmbedsMisses_AndPreservesOrder()
    {
        var inner = new CountingProvider();
        var provider = new CachingEmbeddingProvider(inner, new InMemoryEmbeddingCache());

        // Pre-populate the cache with "b" via a single embed.
        var cachedB = await provider.EmbedAsync("b");
        Assert.Equal(1, inner.EmbeddedCount);

        var texts = new[] { "a", "b", "c" };
        var results = await provider.EmbedBatchAsync(texts);

        // Only "a" and "c" should have been embedded by the inner provider this time.
        Assert.Equal(3, inner.EmbeddedCount);
        Assert.Equal(3, results.Count);

        // The "b" entry comes from cache and matches the earlier single embed.
        Assert.Equal(cachedB, results[1]);

        // Order is preserved: each result corresponds to its input text length.
        Assert.Equal(1f, results[0][0]); // "a"
        Assert.Equal(1f, results[1][0]); // "b"
        Assert.Equal(1f, results[2][0]); // "c"
    }

    [Fact]
    public async Task EmbedBatchAsync_EmptyInput_ReturnsEmpty()
    {
        var inner = new CountingProvider();
        var provider = new CachingEmbeddingProvider(inner, new InMemoryEmbeddingCache());

        var results = await provider.EmbedBatchAsync(Array.Empty<string>());

        Assert.Empty(results);
        Assert.Equal(0, inner.EmbeddedCount);
    }

    #endregion
}
