using RAGify.Embeddings;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="InMemoryEmbeddingCache"/>.
/// </summary>
public class InMemoryEmbeddingCacheTests
{
    [Fact]
    public async Task SetGet_RoundTrips()
    {
        var cache = new InMemoryEmbeddingCache();
        var embedding = new[] { 1f, 2f, 3f };

        await cache.SetAsync("key", embedding);
        var result = await cache.GetAsync("key");

        Assert.Same(embedding, result);
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var cache = new InMemoryEmbeddingCache();

        var result = await cache.GetAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        var cache = new InMemoryEmbeddingCache();
        await cache.SetAsync("a", new[] { 1f });
        await cache.SetAsync("b", new[] { 2f });

        await cache.ClearAsync();

        Assert.Null(await cache.GetAsync("a"));
        Assert.Null(await cache.GetAsync("b"));
    }

    [Fact]
    public async Task MaxEntries_Cap_EvictsOldest()
    {
        var cache = new InMemoryEmbeddingCache(maxEntries: 2);

        await cache.SetAsync("a", new[] { 1f });
        await cache.SetAsync("b", new[] { 2f });
        await cache.SetAsync("c", new[] { 3f });

        // "a" was the oldest insertion and should have been evicted.
        Assert.Null(await cache.GetAsync("a"));
        Assert.NotNull(await cache.GetAsync("b"));
        Assert.NotNull(await cache.GetAsync("c"));
    }
}
