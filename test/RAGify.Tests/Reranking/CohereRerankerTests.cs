using System.Net;
using System.Text;
using System.Text.Json;
using RAGify.Reranking;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="CohereReranker"/>.
/// </summary>
public class CohereRerankerTests
{
    [Fact]
    public async Task RerankAsync_MapsIndicesScoresAndDocuments()
    {
        var responseJson = """
        {
            "results": [
                { "index": 2, "relevance_score": 0.95 },
                { "index": 0, "relevance_score": 0.42 }
            ]
        }
        """;

        var handler = new StubHandler(responseJson);
        var httpClient = new HttpClient(handler);
        var reranker = new CohereReranker("test-key", "rerank-english-v3.0", httpClient: httpClient);

        var documents = new[] { "first document", "second document", "third document" };

        var results = await reranker.RerankAsync("a query", documents, topK: 2);

        Assert.Equal(2, results.Count);

        // First result maps to original index 2.
        Assert.Equal(2, results[0].Index);
        Assert.Equal(0.95, results[0].Score, 5);
        Assert.Equal("third document", results[0].Document);

        // Second result maps to original index 0.
        Assert.Equal(0, results[1].Index);
        Assert.Equal(0.42, results[1].Score, 5);
        Assert.Equal("first document", results[1].Document);
    }

    [Fact]
    public async Task RerankAsync_SendsModelQueryAndTopN()
    {
        var responseJson = """{ "results": [] }""";

        var handler = new StubHandler(responseJson);
        var httpClient = new HttpClient(handler);
        var reranker = new CohereReranker("test-key", "rerank-english-v3.0", httpClient: httpClient);

        var documents = new[] { "doc a", "doc b", "doc c", "doc d" };

        await reranker.RerankAsync("find relevant", documents, topK: 2);

        Assert.NotNull(handler.LastRequestBody);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("rerank-english-v3.0", root.GetProperty("model").GetString());
        Assert.Equal("find relevant", root.GetProperty("query").GetString());
        Assert.Equal(2, root.GetProperty("top_n").GetInt32());
        Assert.Equal(4, root.GetProperty("documents").GetArrayLength());

        // The relative request path should target the rerank endpoint.
        Assert.NotNull(handler.LastRequestUri);
        Assert.EndsWith("rerank", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RerankAsync_EmptyDocuments_ReturnsEmptyWithoutCallingApi()
    {
        var handler = new StubHandler("""{ "results": [] }""");
        var httpClient = new HttpClient(handler);
        var reranker = new CohereReranker("test-key", httpClient: httpClient);

        var results = await reranker.RerankAsync("query", Array.Empty<string>(), topK: 5);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    #region Stub-Handler

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StubHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public string? LastRequestBody { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;

            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    #endregion
}
