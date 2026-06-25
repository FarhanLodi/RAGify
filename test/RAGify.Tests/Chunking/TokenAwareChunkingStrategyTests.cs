using RAGify.Chunking;
using RAGify.Core;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="TokenAwareChunkingStrategy"/>.
/// </summary>
public class TokenAwareChunkingStrategyTests
{
    private static Document MakeDoc(string text) => Document.FromText(text, "doc-1", "src");

    [Fact]
    public async Task ChunkAsync_TinyTokenBudget_ProducesMultipleChunks()
    {
        var options = new ChunkingOptions { ChunkSize = 5, OverlapSize = 1 };
        var strategy = new TokenAwareChunkingStrategy(options);

        var text = "The cat sat on the mat. The dog ran across the yard quickly. Birds fly high above the trees.";

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.True(chunks.Count > 1, "Expected multiple chunks for a tiny token budget.");

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
            Assert.Equal("doc-1", chunks[i].DocumentId);
            Assert.False(string.IsNullOrWhiteSpace(chunks[i].Text));
        }
    }

    [Fact]
    public async Task ChunkAsync_CustomTokenCounter_IsHonored()
    {
        // Token counter that treats every word as exactly one token.
        var options = new ChunkingOptions { ChunkSize = 3, OverlapSize = 0 };
        var strategy = new TokenAwareChunkingStrategy(options)
        {
            TokenCounter = s => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length
        };

        var text = "one two three four five six seven eight nine";

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        // 9 words, 3 per chunk, no overlap => exactly 3 chunks of 3 words each.
        Assert.Equal(3, chunks.Count);
        foreach (var chunk in chunks)
        {
            int wordCount = chunk.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.Equal(3, wordCount);
        }
    }

    [Fact]
    public async Task ChunkAsync_OverlapGreaterThanBudget_Terminates()
    {
        var options = new ChunkingOptions { ChunkSize = 4, OverlapSize = 50 };
        var strategy = new TokenAwareChunkingStrategy(options);

        var text = string.Join(" ", Enumerable.Repeat("word", 100));

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public async Task ChunkAsync_VeryLongSingleWord_EmittedAlone()
    {
        var options = new ChunkingOptions { ChunkSize = 3, OverlapSize = 1 };
        var strategy = new TokenAwareChunkingStrategy(options);

        var hugeWord = new string('z', 200); // ~50 estimated tokens, far above budget
        var text = $"short {hugeWord} ending";

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Text == hugeWord);
    }

    [Fact]
    public async Task ChunkAsync_EmptyContent_ReturnsEmptyList()
    {
        var strategy = new TokenAwareChunkingStrategy(new ChunkingOptions());

        var chunks = await strategy.ChunkAsync(MakeDoc("   "));

        Assert.Empty(chunks);
    }
}
