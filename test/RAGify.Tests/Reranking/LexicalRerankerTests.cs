using RAGify.Reranking;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="LexicalReranker"/>.
/// </summary>
public class LexicalRerankerTests
{
    [Fact]
    public async Task RerankAsync_RanksMatchingDocumentFirst()
    {
        var reranker = new LexicalReranker();
        var documents = new[]
        {
            "The cat sat quietly on the warm mat.",
            "Quantum entanglement describes correlated particles.",
            "A quick brown fox jumps over the lazy dog."
        };

        var results = await reranker.RerankAsync("quantum entanglement particles", documents, topK: 3);

        Assert.NotEmpty(results);
        // The clearly matching document is at original index 1.
        Assert.Equal(1, results[0].Index);
        Assert.Equal(documents[1], results[0].Document);
        Assert.True(results[0].Score > 0.0);
    }

    [Fact]
    public async Task RerankAsync_RespectsTopK()
    {
        var reranker = new LexicalReranker();
        var documents = new[]
        {
            "alpha beta gamma",
            "beta gamma delta",
            "gamma delta epsilon",
            "delta epsilon zeta"
        };

        var results = await reranker.RerankAsync("gamma delta", documents, topK: 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task RerankAsync_EmptyInput_ReturnsEmpty()
    {
        var reranker = new LexicalReranker();

        var results = await reranker.RerankAsync("anything", Array.Empty<string>(), topK: 5);

        Assert.Empty(results);
    }

    [Fact]
    public async Task RerankAsync_IsDeterministic()
    {
        var reranker = new LexicalReranker();
        var documents = new[]
        {
            "red green blue",
            "green blue yellow",
            "blue yellow orange"
        };

        var first = await reranker.RerankAsync("blue green", documents, topK: 3);
        var second = await reranker.RerankAsync("blue green", documents, topK: 3);

        Assert.Equal(
            first.Select(r => r.Index),
            second.Select(r => r.Index));
    }
}
