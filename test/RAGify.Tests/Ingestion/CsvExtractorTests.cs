using System.Text;
using RAGify.Ingestion;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="CsvExtractor"/>.
/// </summary>
public class CsvExtractorTests
{
    [Fact]
    public void CanExtract_CsvAndTsv_ReturnsTrue()
    {
        var extractor = new CsvExtractor();

        Assert.True(extractor.CanExtract("data.csv"));
        Assert.True(extractor.CanExtract("data.TSV"));
        Assert.False(extractor.CanExtract("data.json"));
    }

    [Fact]
    public async Task ExtractAsync_QuotedCommaField_ParsesCorrectly()
    {
        // The second column value contains a comma inside quotes, plus an escaped quote.
        var csv =
            "Name,Description\n" +
            "Widget,\"A small, useful \"\"gadget\"\"\"\n" +
            "Gizmo,Simple\n";

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        await File.WriteAllTextAsync(tempFile, csv, Encoding.UTF8);

        try
        {
            var extractor = new CsvExtractor();
            var result = await extractor.ExtractAsync(tempFile);

            // Header: value lines joined with " | ".
            Assert.Contains("Name: Widget | Description: A small, useful \"gadget\"", result);
            Assert.Contains("Name: Gizmo | Description: Simple", result);
            // Header row itself should not be emitted as a record line.
            Assert.DoesNotContain("Name: Name", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_Tsv_UsesTabDelimiter()
    {
        var tsv =
            "Col1\tCol2\n" +
            "a\tb\n";

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tsv");
        await File.WriteAllTextAsync(tempFile, tsv, Encoding.UTF8);

        try
        {
            var extractor = new CsvExtractor();
            var result = await extractor.ExtractAsync(tempFile);

            Assert.Contains("Col1: a | Col2: b", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_SingleRow_JoinsFields()
    {
        var csv = "alpha,beta,gamma\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var extractor = new CsvExtractor();

        var result = await extractor.ExtractAsync(stream);

        Assert.Equal("alpha | beta | gamma", result);
    }
}
