using System.Text;
using RAGify.Ingestion;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="MarkdownExtractor"/>.
/// </summary>
public class MarkdownExtractorTests
{
    [Fact]
    public void CanExtract_MarkdownExtensions_ReturnsTrue()
    {
        var extractor = new MarkdownExtractor();

        Assert.True(extractor.CanExtract("notes.md"));
        Assert.True(extractor.CanExtract("README.MARKDOWN"));
        Assert.False(extractor.CanExtract("data.txt"));
    }

    [Fact]
    public async Task ExtractAsync_StripsHeadingsEmphasisAndLinks()
    {
        var markdown =
            "# Title\n\n" +
            "Some **bold** and *italic* text with `code`.\n\n" +
            "See [the docs](https://example.com/docs) for more.\n\n" +
            "- bullet one\n" +
            "- bullet two\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));
        var extractor = new MarkdownExtractor();

        var result = await extractor.ExtractAsync(stream);

        // Heading markers stripped.
        Assert.DoesNotContain("#", result);
        // Emphasis markers stripped, text kept.
        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("`", result);
        Assert.Contains("bold", result);
        Assert.Contains("italic", result);
        Assert.Contains("code", result);
        // Link rendered as its text only.
        Assert.Contains("the docs", result);
        Assert.DoesNotContain("https://example.com/docs", result);
        Assert.DoesNotContain("](", result);
        // List bullet markers stripped, text kept.
        Assert.Contains("bullet one", result);
        Assert.Contains("Title", result);
    }

    [Fact]
    public async Task ExtractAsync_FencedCodeBlock_KeepsCodeDropsFences()
    {
        var markdown =
            "Intro line.\n\n" +
            "```csharp\n" +
            "var x = 1;\n" +
            "```\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));
        var extractor = new MarkdownExtractor();

        var result = await extractor.ExtractAsync(stream);

        Assert.Contains("var x = 1;", result);
        Assert.DoesNotContain("```", result);
    }
}
