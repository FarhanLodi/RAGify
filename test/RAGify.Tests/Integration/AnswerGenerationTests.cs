using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Tests;

/// <summary>
/// End-to-end integration tests for the generation ("G" in RAG) stage: answering and streaming
/// over retrieved context using a deterministic, offline echo LLM provider.
/// </summary>
public class AnswerGenerationTests
{
    private const string GardeningDocId = "gardening";

    private const string GardeningText =
        "Gardening involves planting seeds, watering soil, and pruning shrubs in spring. " +
        "Healthy gardens need sunlight, compost, and regular weeding throughout the season. " +
        "Tomatoes, basil, and peppers grow well in raised beds with rich, fertile soil.";

    private static IRagify BuildWithLlm(EchoLlmProvider llm) =>
        new RagifyConfig()
            .WithChunking(ChunkingStrategyType.SentenceAware, new ChunkingOptions { ChunkSize = 200, OverlapSize = 20 })
            .WithEmbeddings(new IntegrationHashEmbeddingProvider(dimension: 64))
            .WithInMemoryVectorStore()
            .WithLlm(llm)
            .Build();

    private static IRagify BuildWithoutLlm() =>
        new RagifyConfig()
            .WithChunking(ChunkingStrategyType.SentenceAware)
            .WithEmbeddings(new IntegrationHashEmbeddingProvider(dimension: 64))
            .WithInMemoryVectorStore()
            .Build();

    private static IDocument GardeningDoc() => Document.FromText(GardeningText, GardeningDocId, "gardening.txt");

    [Fact]
    public async Task AnswerAsync_GeneratesGroundedAnswerFromRetrievedContext()
    {
        var llm = new EchoLlmProvider();
        var ragify = BuildWithLlm(llm);

        await ragify.IngestAsync(GardeningDoc());

        var result = await ragify.AnswerAsync("How should I water soil and prune shrubs when gardening?");

        // The answer is populated and produced by the echo provider.
        Assert.False(string.IsNullOrWhiteSpace(result.Answer));

        Assert.NotNull(result.Generation);
        Assert.Equal("echo", result.Generation!.Model);
        Assert.NotNull(result.Generation.CompletionTokens);
        Assert.True(result.Generation.CompletionTokens > 0);

        // The answer is grounded: retrieved context is non-empty...
        Assert.NotEmpty(result.Context);
        Assert.All(result.Context, r => Assert.Equal(GardeningDocId, r.Chunk.DocumentId));

        // ...and that context was actually passed into the prompt. The echo provider reports the
        // number of numbered context markers (e.g. "[1]") it saw in the messages.
        Assert.Contains("context_markers=", result.Answer);
        Assert.DoesNotContain("context_markers=0", result.Answer);

        // The prompt handed to the LLM included a user message echoing the question.
        Assert.NotNull(llm.LastMessages);
        Assert.Contains(llm.LastMessages!, m => m.Role == ChatRole.User && m.Content.Contains("gardening", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StreamAnswerAsync_YieldsNonEmptyConcatenatedAnswer()
    {
        var llm = new EchoLlmProvider();
        var ragify = BuildWithLlm(llm);

        await ragify.IngestAsync(GardeningDoc());

        var fragments = new List<string>();
        await foreach (var fragment in ragify.StreamAnswerAsync("What grows well in raised beds with fertile soil?"))
        {
            fragments.Add(fragment);
        }

        Assert.NotEmpty(fragments);

        var full = string.Concat(fragments);
        Assert.False(string.IsNullOrWhiteSpace(full));
        Assert.StartsWith("ANSWER:", full);

        // Streaming also runs through retrieval, so context markers must have reached the prompt.
        Assert.DoesNotContain("context_markers=0", full);
    }

    [Fact]
    public async Task AnswerAsync_WithoutLlmProvider_Throws()
    {
        var ragify = BuildWithoutLlm();

        await ragify.IngestAsync(GardeningDoc());

        await Assert.ThrowsAsync<InvalidOperationException>(() => ragify.AnswerAsync("any question"));
    }

    [Fact]
    public async Task StreamAnswerAsync_WithoutLlmProvider_Throws()
    {
        var ragify = BuildWithoutLlm();

        await ragify.IngestAsync(GardeningDoc());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in ragify.StreamAnswerAsync("any question"))
            {
                // The exception is thrown when enumeration begins.
            }
        });
    }
}
