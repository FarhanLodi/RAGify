using System.Text;
using RAGify.Ingestion;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="JsonExtractor"/>.
/// </summary>
public class JsonExtractorTests
{
    [Fact]
    public void CanExtract_JsonAndJsonl_ReturnsTrue()
    {
        var extractor = new JsonExtractor();

        Assert.True(extractor.CanExtract("data.json"));
        Assert.True(extractor.CanExtract("data.JSONL"));
        Assert.False(extractor.CanExtract("data.csv"));
    }

    [Fact]
    public async Task ExtractAsync_NestedObject_FlattensToDottedPaths()
    {
        var json = """
        { "a": { "b": 1, "c": "hello" }, "tags": ["x", "y"] }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var extractor = new JsonExtractor();

        var result = await extractor.ExtractAsync(stream);
        var lines = result.Split('\n');

        Assert.Contains("a.b: 1", lines);
        Assert.Contains("a.c: hello", lines);
        Assert.Contains("tags[0]: x", lines);
        Assert.Contains("tags[1]: y", lines);
    }

    [Fact]
    public async Task ExtractAsync_Jsonl_ProcessesEachLine()
    {
        var jsonl =
            "{\"id\": 1, \"name\": \"alpha\"}\n" +
            "{\"id\": 2, \"name\": \"beta\"}\n";

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jsonl");
        await File.WriteAllTextAsync(tempFile, jsonl, Encoding.UTF8);

        try
        {
            var extractor = new JsonExtractor();
            var result = await extractor.ExtractAsync(tempFile);
            var lines = result.Split('\n');

            Assert.Contains("id: 1", lines);
            Assert.Contains("name: alpha", lines);
            Assert.Contains("id: 2", lines);
            Assert.Contains("name: beta", lines);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_InvalidJson_ThrowsInvalidOperationException()
    {
        var invalid = "{ not valid json ";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalid));
        var extractor = new JsonExtractor();

        await Assert.ThrowsAsync<InvalidOperationException>(() => extractor.ExtractAsync(stream));
    }
}
