using System.Net;
using System.Text;
using RAGify.Ingestion;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="WebPageExtractor"/>.
/// </summary>
public class WebPageExtractorTests
{
    [Fact]
    public void CanExtract_HttpUrls_ReturnsTrue()
    {
        using var extractor = new WebPageExtractor();

        Assert.True(extractor.CanExtract("https://x"));
        Assert.True(extractor.CanExtract("HTTP://example.com"));
        Assert.False(extractor.CanExtract("file.txt"));
    }

    [Fact]
    public async Task ExtractAsync_Url_ReturnsCleanedTextWithoutScripts()
    {
        var html =
            "<html><head><title>T</title>" +
            "<style>body { color: red; }</style></head>" +
            "<body><h1>Hello</h1>" +
            "<script>console.log('should be removed');</script>" +
            "<p>World &amp; everyone</p></body></html>";

        var stub = new StubHandler(html);
        var client = new HttpClient(stub);
        using var extractor = new WebPageExtractor(client);

        var result = await extractor.ExtractAsync("https://example.com/page");

        Assert.Contains("Hello", result);
        Assert.Contains("World & everyone", result);
        // Script and style content must be removed.
        Assert.DoesNotContain("should be removed", result);
        Assert.DoesNotContain("color: red", result);
    }

    [Fact]
    public async Task ExtractAsync_Stream_ReturnsCleanedText()
    {
        var html =
            "<html><body><p>Stream content</p>" +
            "<script>var hidden = 1;</script></body></html>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        using var extractor = new WebPageExtractor();

        var result = await extractor.ExtractAsync(stream);

        Assert.Contains("Stream content", result);
        Assert.DoesNotContain("var hidden", result);
    }

    #region Stub-Handler

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _html;

        public StubHandler(string html)
        {
            _html = html;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_html, Encoding.UTF8, "text/html")
            };

            return Task.FromResult(response);
        }
    }

    #endregion
}
