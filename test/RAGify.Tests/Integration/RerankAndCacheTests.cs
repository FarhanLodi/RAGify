using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Tests;

/// <summary>
/// End-to-end integration tests covering optional pipeline stages wired through the public builder:
/// the lexical (BM25) reranker, the in-memory embedding cache, and the newer chunking strategies.
/// All components are in-memory and offline.
/// </summary>
public class RerankAndCacheTests
{
    private const string OceanDocId = "ocean";
    private const string MountainDocId = "mountain";

    private const string OceanText =
        "Oceans cover most of the planet with deep currents, coral reefs, and marine life. " +
        "Whales, dolphins, and tuna migrate across vast saltwater expanses each year. " +
        "Tides rise and fall as the moon pulls on the seawater along every coastline.";

    private const string MountainText =
        "Mountains rise with steep ridges, glaciers, and rocky alpine slopes above the treeline. " +
        "Climbers ascend granite peaks using ropes, crampons, and ice axes in thin air. " +
        "Snow accumulates on summits while valleys below stay green with pine forests.";

    private static IDocument OceanDoc() => Document.FromText(OceanText, OceanDocId, "ocean.txt");
    private static IDocument MountainDoc() => Document.FromText(MountainText, MountainDocId, "mountain.txt");

    [Fact]
    public async Task QueryAsync_WithLexicalReranker_ReturnsSensibleContext()
    {
        var ragify = new RagifyConfig()
            .WithChunking(ChunkingStrategyType.SentenceAware, new ChunkingOptions { ChunkSize = 200, OverlapSize = 20 })
            .WithEmbeddings(new IntegrationHashEmbeddingProvider(dimension: 64))
            .WithInMemoryVectorStore()
            .WithLexicalReranker()
            .Build();

        await ragify.IngestBatchAsync(new[] { OceanDoc(), MountainDoc() });

        var result = await ragify.QueryAsync("Which whales and dolphins migrate across the ocean currents?");

        // Reranking must not break retrieval: results still come back...
        Assert.NotEmpty(result.Context);
        Assert.All(result.Context, r => Assert.False(string.IsNullOrWhiteSpace(r.Source)));

        // ...and the ocean document (which the query overlaps) is represented in the results.
        Assert.Contains(result.Context, r => r.Chunk.DocumentId == OceanDocId);

        // The top reranked result should be the ocean document given the lexical overlap.
        Assert.Equal(OceanDocId, result.Context[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task QueryAsync_WithEmbeddingCache_ServesRepeatedQueryFromCache()
    {
        // Hold a reference to the INNER provider so we can inspect how many times it actually
        // computed an embedding for a specific text. The builder wraps it in a CachingEmbeddingProvider.
        var inner = new IntegrationHashEmbeddingProvider(dimension: 64);

        var ragify = new RagifyConfig()
            .WithChunking(ChunkingStrategyType.SentenceAware, new ChunkingOptions { ChunkSize = 200, OverlapSize = 20 })
            .WithEmbeddings(inner)
            .WithInMemoryVectorStore()
            .WithInMemoryEmbeddingCache()
            .Build();

        await ragify.IngestAsync(OceanDoc());

        const string query = "deep ocean currents and coral reefs near the coastline";

        // The query text is not a chunk text, so before the first query the inner provider has
        // never embedded it.
        Assert.Equal(0, inner.EmbedCountFor(query));

        var first = await ragify.QueryAsync(query);
        Assert.NotEmpty(first.Context);

        // First query is a cache miss: the inner provider embedded the query exactly once.
        Assert.Equal(1, inner.EmbedCountFor(query));

        var second = await ragify.QueryAsync(query);
        Assert.NotEmpty(second.Context);

        // Second identical query is a cache hit: the inner provider's count for that exact query
        // text did NOT increase.
        Assert.Equal(1, inner.EmbedCountFor(query));
    }

    [Theory]
    [InlineData(ChunkingStrategyType.Recursive)]
    [InlineData(ChunkingStrategyType.TokenAware)]
    [InlineData(ChunkingStrategyType.FixedSize)]
    [InlineData(ChunkingStrategyType.SlidingWindow)]
    public async Task QueryAsync_WithVariousChunkers_RetrievesContext(ChunkingStrategyType strategy)
    {
        var ragify = new RagifyConfig()
            .WithChunking(strategy, new ChunkingOptions { ChunkSize = 180, OverlapSize = 20 })
            .WithEmbeddings(new IntegrationHashEmbeddingProvider(dimension: 64))
            .WithInMemoryVectorStore()
            .Build();

        await ragify.IngestBatchAsync(new[] { OceanDoc(), MountainDoc() });

        // Ingestion produced chunks for the ocean document.
        Assert.NotEmpty(await ragify.GetChunksAsync(OceanDocId));

        var result = await ragify.QueryAsync("Climbers ascend granite peaks and glaciers above the treeline.");

        Assert.NotEmpty(result.Context);
        Assert.Contains(result.Context, r => r.Chunk.DocumentId == MountainDocId);
    }

    [Fact]
    public async Task QueryAsync_WithMarkdownChunker_RetrievesContext()
    {
        const string markdown =
            "# Ocean Life\n\n" +
            "Oceans cover most of the planet with deep currents, coral reefs, and marine life. " +
            "Whales, dolphins, and tuna migrate across vast saltwater expanses each year.\n\n" +
            "## Tides\n\n" +
            "Tides rise and fall as the moon pulls on the seawater along every coastline.";

        var ragify = new RagifyConfig()
            .WithChunking(ChunkingStrategyType.Markdown, new ChunkingOptions { ChunkSize = 200, OverlapSize = 20 })
            .WithEmbeddings(new IntegrationHashEmbeddingProvider(dimension: 64))
            .WithInMemoryVectorStore()
            .Build();

        await ragify.IngestAsync(Document.FromText(markdown, OceanDocId, "ocean.md"));

        Assert.NotEmpty(await ragify.GetChunksAsync(OceanDocId));

        var result = await ragify.QueryAsync("How do tides and ocean currents move whales and dolphins?");

        Assert.NotEmpty(result.Context);
        Assert.All(result.Context, r => Assert.Equal(OceanDocId, r.Chunk.DocumentId));
    }
}
