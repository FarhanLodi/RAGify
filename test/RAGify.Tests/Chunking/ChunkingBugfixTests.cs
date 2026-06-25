using RAGify.Abstractions;
using RAGify.Chunking;
using RAGify.Core;

namespace RAGify.Tests;

/// <summary>
/// Regression tests for chunking correctness bugs in
/// <see cref="FixedSizeChunkingStrategy"/> and <see cref="SentenceAwareChunkingStrategy"/>.
/// </summary>
public class ChunkingBugfixTests
{
    private static Document MakeDocument(string text) => Document.FromText(text, "doc-1", "src");

    #region FixedSize

    [Fact]
    public async Task FixedSize_DoesNotEmitDuplicateTrailingChunk()
    {
        // Length 25 with ChunkSize 10 / OverlapSize 5 previously stepped past the end and
        // emitted a final chunk that duplicated the tail of the previous chunk.
        var text = "ABCDEFGHIJKLMNOPQRSTUVWXY"; // 25 chars
        var options = new ChunkingOptions { ChunkSize = 10, OverlapSize = 5 };
        var strategy = new FixedSizeChunkingStrategy(options);

        var chunks = await strategy.ChunkAsync(MakeDocument(text));

        Assert.NotEmpty(chunks);

        // No two consecutive chunks are identical.
        for (int i = 1; i < chunks.Count; i++)
        {
            Assert.NotEqual(chunks[i - 1].Text, chunks[i].Text);
        }

        // The final chunk must not be a pure duplicate of the previous chunk's tail.
        if (chunks.Count >= 2)
        {
            var last = chunks[^1].Text;
            var prev = chunks[^2].Text;
            Assert.False(prev.EndsWith(last, StringComparison.Ordinal) && last.Length < prev.Length,
                "Final chunk duplicates the tail of the previous chunk.");
        }
    }

    [Fact]
    public async Task FixedSize_ReconstructionCoversText()
    {
        var text = "The quick brown fox jumps over the lazy dog near the river bank today.";
        var options = new ChunkingOptions { ChunkSize = 12, OverlapSize = 4 };
        var strategy = new FixedSizeChunkingStrategy(options);

        var chunks = await strategy.ChunkAsync(MakeDocument(text));

        // Every character of the source text must appear, in order, across the chunks.
        // Walk through the original text consuming each chunk's content allowing for overlap.
        int covered = 0;
        foreach (var chunk in chunks)
        {
            // The chunk must be a substring of the original starting at or before `covered`.
            int idx = text.IndexOf(chunk.Text, Math.Max(0, covered - options.ChunkSize), StringComparison.Ordinal);
            Assert.True(idx >= 0, $"Chunk '{chunk.Text}' not found in source.");
            covered = Math.Max(covered, idx + chunk.Text.Length);
        }

        Assert.Equal(text.Length, covered);
    }

    [Fact]
    public async Task FixedSize_TerminatesWhenOverlapGreaterThanOrEqualChunkSize()
    {
        var text = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; // 26 chars
        var options = new ChunkingOptions { ChunkSize = 5, OverlapSize = 10 }; // overlap >= chunk size
        var strategy = new FixedSizeChunkingStrategy(options);

        // Must terminate (no infinite loop) and produce forward-progressing chunks.
        var chunks = await strategy.ChunkAsync(MakeDocument(text));

        Assert.NotEmpty(chunks);
        // Sequential indices and full coverage of the text end.
        Assert.Equal(text[^1], chunks[^1].Text[^1]);
    }

    [Fact]
    public async Task FixedSize_EmptyContent_ReturnsEmpty()
    {
        var options = new ChunkingOptions { ChunkSize = 10, OverlapSize = 3 };
        var strategy = new FixedSizeChunkingStrategy(options);

        var chunks = await strategy.ChunkAsync(MakeDocument("   "));

        Assert.Empty(chunks);
    }

    #endregion

    #region SentenceAware

    [Fact]
    public async Task SentenceAware_RetainsSentencePunctuation()
    {
        var text = "Hello world. Is this working? Yes it is! Final sentence.";
        var options = new ChunkingOptions { ChunkSize = 1000, OverlapSize = 0, MaxSentencesPerChunk = 10 };
        var strategy = new SentenceAwareChunkingStrategy(options);

        var chunks = await strategy.ChunkAsync(MakeDocument(text));

        Assert.NotEmpty(chunks);
        var joined = string.Join(" ", chunks.Select(c => c.Text));

        Assert.Contains(".", joined);
        Assert.Contains("?", joined);
        Assert.Contains("!", joined);
    }

    [Fact]
    public async Task SentenceAware_OversizedSentence_SplitIntoMultipleChunks()
    {
        // A single sentence much longer than ChunkSize and with no internal terminators.
        var longSentence = new string('a', 250) + ".";
        var options = new ChunkingOptions { ChunkSize = 50, OverlapSize = 0, MaxSentencesPerChunk = 5 };
        var strategy = new SentenceAwareChunkingStrategy(options);

        var chunks = await strategy.ChunkAsync(MakeDocument(longSentence));

        Assert.True(chunks.Count > 1, "Oversized sentence should be split into multiple chunks.");
        foreach (var chunk in chunks)
        {
            Assert.False(string.IsNullOrWhiteSpace(chunk.Text));
        }

        // Indices remain sequential from 0.
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
        }
    }

    [Fact]
    public async Task SentenceAware_EmptyContent_ReturnsEmpty()
    {
        var options = new ChunkingOptions { ChunkSize = 100, OverlapSize = 0 };
        var strategy = new SentenceAwareChunkingStrategy(options);

        var chunks = await strategy.ChunkAsync(MakeDocument(""));

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task SentenceAware_FinalSentenceWithoutTerminator_IsPreserved()
    {
        var text = "First sentence. Second one without a period";
        var options = new ChunkingOptions { ChunkSize = 1000, OverlapSize = 0, MaxSentencesPerChunk = 10 };
        var strategy = new SentenceAwareChunkingStrategy(options);

        var chunks = await strategy.ChunkAsync(MakeDocument(text));

        Assert.NotEmpty(chunks);
        var joined = string.Join(" ", chunks.Select(c => c.Text));
        Assert.Contains("Second one without a period", joined);
    }

    [Fact]
    public async Task SentenceAware_SequentialIndices()
    {
        var text = "One. Two. Three. Four. Five. Six. Seven. Eight.";
        var options = new ChunkingOptions { ChunkSize = 12, OverlapSize = 0, MaxSentencesPerChunk = 2 };
        var strategy = new SentenceAwareChunkingStrategy(options);

        var chunks = await strategy.ChunkAsync(MakeDocument(text));

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
        }
    }

    #endregion
}
