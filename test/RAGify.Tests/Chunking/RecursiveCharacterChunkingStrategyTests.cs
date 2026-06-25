using RAGify.Chunking;
using RAGify.Core;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="RecursiveCharacterChunkingStrategy"/>.
/// </summary>
public class RecursiveCharacterChunkingStrategyTests
{
    private static Document MakeDoc(string text) => Document.FromText(text, "doc-1", "src");

    [Fact]
    public async Task ChunkAsync_LongParagraphText_ProducesMultipleBoundedChunks()
    {
        var options = new ChunkingOptions { ChunkSize = 120, OverlapSize = 20 };
        var strategy = new RecursiveCharacterChunkingStrategy(options);

        // Several paragraph-separated blocks that together far exceed the chunk size.
        var paragraph = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 6));
        var text = string.Join("\n\n", Enumerable.Repeat(paragraph, 8));

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.True(chunks.Count > 1, "Expected the long text to produce multiple chunks.");

        // Each chunk should respect the size bound, allowing for the documented overlap carry.
        int bound = options.ChunkSize + options.OverlapSize;
        foreach (var chunk in chunks)
        {
            Assert.False(string.IsNullOrWhiteSpace(chunk.Text));
            Assert.True(chunk.Text.Length <= bound,
                $"Chunk length {chunk.Text.Length} exceeded bound {bound}.");
        }
    }

    [Fact]
    public async Task ChunkAsync_AssignsSequentialIndexAndPropagatesDocumentId()
    {
        var options = new ChunkingOptions { ChunkSize = 50, OverlapSize = 10 };
        var strategy = new RecursiveCharacterChunkingStrategy(options);

        var text = string.Join("\n\n", Enumerable.Repeat("Sentence one here. Sentence two here. Sentence three here.", 10));

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.NotEmpty(chunks);
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
            Assert.Equal("doc-1", chunks[i].DocumentId);
        }
    }

    [Fact]
    public async Task ChunkAsync_VeryLongSingleWord_Terminates()
    {
        var options = new ChunkingOptions { ChunkSize = 30, OverlapSize = 10 };
        var strategy = new RecursiveCharacterChunkingStrategy(options);

        var text = new string('x', 500);

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
            Assert.False(string.IsNullOrWhiteSpace(chunk.Text));
    }

    [Fact]
    public async Task ChunkAsync_OverlapGreaterThanSize_Terminates()
    {
        var options = new ChunkingOptions { ChunkSize = 40, OverlapSize = 100 };
        var strategy = new RecursiveCharacterChunkingStrategy(options);

        var text = string.Join(" ", Enumerable.Repeat("word", 200));

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public async Task ChunkAsync_EmptyContent_ReturnsEmptyList()
    {
        var strategy = new RecursiveCharacterChunkingStrategy(new ChunkingOptions());

        var chunks = await strategy.ChunkAsync(MakeDoc("   "));

        Assert.Empty(chunks);
    }
}
