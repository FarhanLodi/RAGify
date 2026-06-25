using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Tests;

/// <summary>
/// End-to-end integration tests for the core RAG retrieval pipeline using only in-memory,
/// offline components: real chunking, the real <c>InMemoryVectorStore</c>, and a deterministic
/// hash-based embedding provider.
/// </summary>
public class RagPipelineTests
{
    // Each document uses a distinctive, repeated vocabulary so the hash embeddings cleanly
    // separate topics. Content is comfortably above the retrieval engine's minimum-length filters.
    private const string AstronomyDocId = "astronomy";
    private const string CookingDocId = "cooking";
    private const string FinanceDocId = "finance";

    private const string AstronomyText =
        "Astronomy studies planets, stars, galaxies, and comets. " +
        "Telescopes observe distant galaxies and nebulae across the cosmos. " +
        "Astronomers track orbits of planets and the trajectories of comets through space.";

    private const string CookingText =
        "Cooking recipes combine flour, butter, sugar, and eggs into delicious pastries. " +
        "Baking bread requires kneading dough and proofing yeast before the oven. " +
        "A good recipe balances flavor, seasoning, and careful timing in the kitchen.";

    private const string FinanceText =
        "Finance covers stocks, bonds, interest rates, and investment portfolios. " +
        "Investors diversify portfolios to manage risk across equity and bond markets. " +
        "Compound interest grows savings while inflation erodes purchasing power over time.";

    private static IRagify BuildPipeline() =>
        new RagifyConfig()
            .WithChunking(ChunkingStrategyType.SentenceAware, new ChunkingOptions { ChunkSize = 200, OverlapSize = 20 })
            .WithEmbeddings(new IntegrationHashEmbeddingProvider(dimension: 64))
            .WithInMemoryVectorStore()
            .Build();

    private static IDocument AstronomyDoc() => Document.FromText(AstronomyText, AstronomyDocId, "astronomy.txt");
    private static IDocument CookingDoc() => Document.FromText(CookingText, CookingDocId, "cooking.txt");
    private static IDocument FinanceDoc() => Document.FromText(FinanceText, FinanceDocId, "finance.txt");

    [Fact]
    public async Task QueryAsync_ReturnsContextFromRelevantDocument()
    {
        var ragify = BuildPipeline();

        await ragify.IngestAsync(AstronomyDoc());
        await ragify.IngestBatchAsync(new[] { CookingDoc(), FinanceDoc() });

        // Query vocabulary overlaps strongly with the astronomy document only.
        var result = await ragify.QueryAsync("Which planets and galaxies do astronomers observe with telescopes?");

        Assert.NotNull(result);
        Assert.NotEmpty(result.Context);

        // Every retrieved result should have a populated source (set from the owning document).
        Assert.All(result.Context, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Source));
            Assert.NotNull(r.Chunk);
        });

        // The top result must come from the astronomy document.
        var top = result.Context[0];
        Assert.Equal(AstronomyDocId, top.Chunk.DocumentId);
        Assert.Equal("astronomy.txt", top.Source);
        Assert.Contains("astronom", top.Chunk.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(top.Similarity >= 0.35, $"Top similarity {top.Similarity} should clear the retrieval threshold.");

        // Retrieval metadata should be populated by the QueryAsync path.
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.EffectiveTopK > 0);
        Assert.True(result.Metadata.SimilarityThreshold > 0.0);
        Assert.False(string.IsNullOrWhiteSpace(result.Metadata.QuestionType));
    }

    [Fact]
    public async Task GetIndexedDocumentsAsync_ReturnsAllIngestedDocumentIds()
    {
        var ragify = BuildPipeline();

        await ragify.IngestBatchAsync(new[] { AstronomyDoc(), CookingDoc(), FinanceDoc() });

        var docIds = await ragify.GetIndexedDocumentsAsync();

        // Assert set membership rather than ordering, which is not guaranteed.
        Assert.Equal(
            new HashSet<string> { AstronomyDocId, CookingDocId, FinanceDocId },
            docIds.ToHashSet());
    }

    [Fact]
    public async Task GetChunksAsync_ReturnsChunksOrderedByIndex()
    {
        var ragify = BuildPipeline();

        await ragify.IngestAsync(FinanceDoc());

        var chunks = await ragify.GetChunksAsync(FinanceDocId);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal(FinanceDocId, c.DocumentId));

        // Indices must be strictly increasing (i.e. ordered by Index).
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
        }
    }

    [Fact]
    public async Task GetChunksAsync_UnknownDocument_ReturnsEmpty()
    {
        var ragify = BuildPipeline();

        await ragify.IngestAsync(AstronomyDoc());

        var chunks = await ragify.GetChunksAsync("does-not-exist");

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllData_AndQueryReturnsEmptyContext()
    {
        var ragify = BuildPipeline();

        await ragify.IngestBatchAsync(new[] { AstronomyDoc(), CookingDoc(), FinanceDoc() });

        // Sanity: retrieval works before clearing.
        var before = await ragify.QueryAsync("planets galaxies telescopes astronomers");
        Assert.NotEmpty(before.Context);

        await ragify.ClearAsync();

        // After clear, indexed documents are gone...
        Assert.Empty(await ragify.GetIndexedDocumentsAsync());

        // ...and the SAME query returns no context (validates the ClearAsync stale-cache fix:
        // the retrieval engine's chunk cache is cleared so no stale chunks resurface).
        var after = await ragify.QueryAsync("planets galaxies telescopes astronomers");
        Assert.Empty(after.Context);
    }

    [Fact]
    public async Task QueryAsync_AfterClearAndReingest_ReturnsFreshContext()
    {
        var ragify = BuildPipeline();

        await ragify.IngestAsync(AstronomyDoc());
        await ragify.ClearAsync();
        await ragify.IngestAsync(CookingDoc());

        var result = await ragify.QueryAsync("baking bread recipe with flour and butter");

        Assert.NotEmpty(result.Context);
        // Only cooking content should be retrievable; astronomy was cleared.
        Assert.All(result.Context, r => Assert.Equal(CookingDocId, r.Chunk.DocumentId));
    }
}
