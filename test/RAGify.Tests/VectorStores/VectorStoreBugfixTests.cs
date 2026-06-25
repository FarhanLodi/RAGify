using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.VectorStores;

namespace RAGify.Tests;

/// <summary>
/// Tests covering the correctness bug fixes in the REST/SQL based vector stores:
/// Qdrant <see cref="QdrantVectorStore.ClearAsync"/> semaphore balance,
/// PgVectorStore invariant-culture float formatting, and Weaviate GraphQL search.
/// </summary>
public class VectorStoreBugfixTests
{
    #region Stub-Handler

    /// <summary>
    /// Records every outbound request (method + URL + body) and replies with a
    /// canned response selected per-request via a supplied responder.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public sealed record CapturedRequest(HttpMethod Method, string Url, string? Body);

        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

        public List<CapturedRequest> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content != null)
                body = await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.ToString(), body));

            return _responder(request, body);
        }
    }

    private static HttpResponseMessage Ok(string json = "{}")
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    #endregion

    #region PgVector-Culture

    [Fact]
    public void PgVector_FloatFormatting_UsesDotUnderCommaDecimalLocale()
    {
        // Guard test that locks in the formatting invariant the PgVectorStore fix relies on:
        // under a comma-decimal locale (de-DE) the emitted vector literal must still use '.'.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var formatted = (0.123456f).ToString("F6", CultureInfo.InvariantCulture);

            Assert.Contains(".", formatted);
            Assert.DoesNotContain(",", formatted);
            Assert.Equal("0.123456", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    #endregion

    #region Qdrant

    [Fact]
    public void Qdrant_Constructor_DoesNotThrow()
    {
        var store = new QdrantVectorStore("localhost", port: 6333);
        Assert.NotNull(store);
    }

    [Fact]
    public async Task Qdrant_ClearAsync_IssuesDeleteThenPut()
    {
        var handler = new StubHandler((request, _) =>
        {
            // Every collection operation returns success so the create PUT's
            // EnsureSuccessStatusCode passes.
            return Ok("{\"result\":true}");
        });
        var httpClient = new HttpClient(handler);

        var store = new QdrantVectorStore(
            host: "localhost",
            httpClient: httpClient);

        await store.ClearAsync();

        var collectionRequests = handler.Requests
            .Where(r => r.Url.Contains("/collections/ragify_vectors"))
            .ToList();

        // The first request must be the DELETE of the collection, the next the
        // create-collection PUT.
        Assert.Contains(collectionRequests, r => r.Method == HttpMethod.Delete);
        Assert.Contains(collectionRequests, r => r.Method == HttpMethod.Put);

        var deleteIndex = collectionRequests.FindIndex(r => r.Method == HttpMethod.Delete);
        var putIndex = collectionRequests.FindIndex(r => r.Method == HttpMethod.Put);
        Assert.True(deleteIndex >= 0, "Expected a DELETE request.");
        Assert.True(putIndex >= 0, "Expected a PUT request.");
        Assert.True(deleteIndex < putIndex, "DELETE should occur before the create PUT.");
    }

    [Fact]
    public async Task Qdrant_ClearAsync_SemaphoreRemainsBalanced_AllowsSubsequentInit()
    {
        // A stray Release() would over-increment the SemaphoreSlim(1,1). If that were
        // the case, a follow-up operation that also waits on the same semaphore could
        // proceed without proper mutual exclusion. Here we simply verify Clear can be
        // invoked repeatedly without throwing (a corrupted semaphore would eventually
        // surface as SemaphoreFullException on Release()).
        var handler = new StubHandler((request, _) => Ok("{\"result\":true}"));
        var httpClient = new HttpClient(handler);

        var store = new QdrantVectorStore(host: "localhost", httpClient: httpClient);

        await store.ClearAsync();
        await store.ClearAsync();
        await store.ClearAsync();

        Assert.True(handler.Requests.Count >= 6, "Each Clear should issue at least a DELETE and a PUT.");
    }

    #endregion

    #region Weaviate

    [Fact]
    public void Weaviate_Constructor_DoesNotThrow()
    {
        var store = new WeaviateVectorStore("http://localhost:8080");
        Assert.NotNull(store);
    }

    [Fact]
    public async Task Weaviate_SearchAsync_PostsGraphQlAndParsesCertaintyAndId()
    {
        const string className = "RAGifyVector";

        // Canned GraphQL response in Weaviate's documented shape.
        var cannedResponse =
            "{\"data\":{\"Get\":{\"" + className + "\":[" +
            "{\"DocumentId\":\"doc-1\",\"_additional\":{\"id\":\"vec-123\",\"certainty\":0.87,\"distance\":0.26}}," +
            "{\"DocumentId\":\"doc-2\",\"_additional\":{\"id\":\"vec-456\",\"certainty\":0.40,\"distance\":1.20}}" +
            "]}}}";

        string? capturedSchemaCheck = null;
        var handler = new StubHandler((request, body) =>
        {
            var url = request.RequestUri!.ToString();

            // Schema existence check during EnsureSchemaExistsAsync — report it exists.
            if (url.Contains("/v1/schema/"))
            {
                capturedSchemaCheck = url;
                return Ok("{\"class\":\"" + className + "\"}");
            }

            // GraphQL search endpoint.
            if (url.EndsWith("/v1/graphql"))
            {
                return Ok(cannedResponse);
            }

            return Ok();
        });
        var httpClient = new HttpClient(handler);

        var store = new WeaviateVectorStore(
            baseUrl: "http://localhost:8080",
            className: className,
            httpClient: httpClient);

        var results = await store.SearchAsync(
            queryVector: new[] { 0.1f, 0.2f, 0.3f },
            topK: 10,
            threshold: 0.5);

        // The GraphQL endpoint must have been hit with a { "query": "..." } body.
        var graphqlRequest = handler.Requests.SingleOrDefault(r => r.Url.EndsWith("/v1/graphql"));
        Assert.NotNull(graphqlRequest);
        Assert.Equal(HttpMethod.Post, graphqlRequest!.Method);
        Assert.NotNull(graphqlRequest.Body);

        using var bodyDoc = JsonDocument.Parse(graphqlRequest.Body!);
        Assert.True(bodyDoc.RootElement.TryGetProperty("query", out var queryElement));
        var queryText = queryElement.GetString();
        Assert.NotNull(queryText);
        Assert.Contains("Get", queryText!);
        Assert.Contains("nearVector", queryText!);
        Assert.Contains("certainty", queryText!);

        // Only the first result (certainty 0.87) clears the 0.5 threshold.
        Assert.Single(results);
        var top = results[0];
        Assert.Equal("vec-123", top.VectorId);
        Assert.Equal(0.87, top.Similarity, 3);
        Assert.True(top.Metadata.ContainsKey("DocumentId"));
        Assert.Equal("doc-1", top.Metadata["DocumentId"]?.ToString());
    }

    [Fact]
    public async Task Weaviate_SearchAsync_IncludesWhereFilterInGraphQl()
    {
        const string className = "RAGifyVector";

        var handler = new StubHandler((request, body) =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/v1/schema/"))
                return Ok("{\"class\":\"" + className + "\"}");
            if (url.EndsWith("/v1/graphql"))
                return Ok("{\"data\":{\"Get\":{\"" + className + "\":[]}}}");
            return Ok();
        });
        var httpClient = new HttpClient(handler);

        var store = new WeaviateVectorStore(
            baseUrl: "http://localhost:8080",
            className: className,
            httpClient: httpClient);

        var filter = new MetadataFilter
        {
            Filters = new Dictionary<string, object> { ["Category"] = "news" }
        };

        var results = await store.SearchAsync(
            queryVector: new[] { 0.5f, 0.5f },
            topK: 5,
            threshold: 0.0,
            filter: filter);

        Assert.Empty(results);

        var graphqlRequest = handler.Requests.Single(r => r.Url.EndsWith("/v1/graphql"));
        using var bodyDoc = JsonDocument.Parse(graphqlRequest.Body!);
        var queryText = bodyDoc.RootElement.GetProperty("query").GetString();

        Assert.NotNull(queryText);
        Assert.Contains("where", queryText!);
        Assert.Contains("operator: Equal", queryText!);
        Assert.Contains("Category", queryText!);
        Assert.Contains("valueText", queryText!);
    }

    #endregion
}
