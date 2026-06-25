using RAGify.Chunking;
using RAGify.Core;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="MarkdownChunkingStrategy"/>.
/// </summary>
public class MarkdownChunkingStrategyTests
{
    private static Document MakeDoc(string text) => Document.FromText(text, "doc-1", "src");

    [Fact]
    public async Task ChunkAsync_HeadingsStartNewChunks()
    {
        var options = new ChunkingOptions { ChunkSize = 2000 };
        var strategy = new MarkdownChunkingStrategy(options);

        var text =
            "# First Heading\n" +
            "Content under the first heading.\n\n" +
            "## Second Heading\n" +
            "Content under the second heading.\n\n" +
            "## Third Heading\n" +
            "Content under the third heading.";

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        // Three headings, each section small enough to be its own chunk.
        Assert.Equal(3, chunks.Count);
        Assert.Contains("First Heading", chunks[0].Text);
        Assert.Contains("Second Heading", chunks[1].Text);
        Assert.Contains("Third Heading", chunks[2].Text);

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
            Assert.Equal("doc-1", chunks[i].DocumentId);
        }
    }

    [Fact]
    public async Task ChunkAsync_FencedCodeBlock_NotSplitAcrossChunks()
    {
        var options = new ChunkingOptions { ChunkSize = 2000 };
        var strategy = new MarkdownChunkingStrategy(options);

        var text =
            "# Code Sample\n" +
            "Here is some code:\n\n" +
            "```csharp\n" +
            "## This is not a heading, it is inside a fence\n" +
            "var x = 1;\n" +
            "var y = 2;\n" +
            "```\n\n" +
            "## Real Heading After Code\n" +
            "More text here.";

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        // The code fence (and its heading-looking line) must live entirely inside one chunk.
        var fenceChunk = chunks.Single(c => c.Text.Contains("var x = 1;"));
        Assert.Contains("var y = 2;", fenceChunk.Text);
        Assert.Contains("## This is not a heading", fenceChunk.Text);

        // The real heading after the fence starts its own chunk.
        Assert.Contains(chunks, c => c.Text.Contains("Real Heading After Code"));
        Assert.True(chunks.Count >= 2);
    }

    [Fact]
    public async Task ChunkAsync_LargeSection_SubSplitWithinBound()
    {
        var options = new ChunkingOptions { ChunkSize = 100 };
        var strategy = new MarkdownChunkingStrategy(options);

        var bigParagraph = string.Join(" ", Enumerable.Repeat("alpha beta gamma delta epsilon", 30));
        var text = "# Big Section\n" + bigParagraph;

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.True(chunks.Count > 1, "Expected the oversized section to be sub-split.");
        // Allow a modest margin for the heading prefix that is added for context.
        int bound = options.ChunkSize * 2;
        foreach (var chunk in chunks)
        {
            Assert.False(string.IsNullOrWhiteSpace(chunk.Text));
            Assert.True(chunk.Text.Length <= bound,
                $"Chunk length {chunk.Text.Length} exceeded bound {bound}.");
        }
    }

    [Fact]
    public async Task ChunkAsync_SequentialIndexFromZero()
    {
        var options = new ChunkingOptions { ChunkSize = 80 };
        var strategy = new MarkdownChunkingStrategy(options);

        var text =
            "# A\nSome content.\n\n# B\nMore content.\n\n# C\nEven more content here today.";

        var chunks = await strategy.ChunkAsync(MakeDoc(text));

        Assert.NotEmpty(chunks);
        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].Index);
    }

    [Fact]
    public async Task ChunkAsync_EmptyContent_ReturnsEmptyList()
    {
        var strategy = new MarkdownChunkingStrategy(new ChunkingOptions());

        var chunks = await strategy.ChunkAsync(MakeDoc(""));

        Assert.Empty(chunks);
    }
}
