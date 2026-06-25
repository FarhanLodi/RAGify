using System.Net;
using RAGify.Embeddings;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="RetryDelegatingHandler"/>.
/// </summary>
public class RetryDelegatingHandlerTests
{
    #region Test-Doubles

    /// <summary>
    /// Returns a predetermined sequence of status codes, one per request, and counts attempts.
    /// </summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;

        public int Attempts { get; private set; }

        public SequenceHandler(params HttpStatusCode[] statuses)
        {
            _statuses = new Queue<HttpStatusCode>(statuses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    #endregion

    #region Tests

    [Fact]
    public async Task SendAsync_RetriesTransientFailures_UntilSuccess()
    {
        var sequenceHandler = new SequenceHandler(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK);

        using var client = new HttpClient(new RetryDelegatingHandler(3, TimeSpan.FromMilliseconds(1))
        {
            InnerHandler = sequenceHandler
        });

        var response = await client.GetAsync("https://example.test/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, sequenceHandler.Attempts);
    }

    [Fact]
    public async Task SendAsync_StopsAfterMaxRetries_ReturnsLastFailure()
    {
        var sequenceHandler = new SequenceHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable);

        using var client = new HttpClient(new RetryDelegatingHandler(2, TimeSpan.FromMilliseconds(1))
        {
            InnerHandler = sequenceHandler
        });

        var response = await client.GetAsync("https://example.test/");

        // Initial attempt + 2 retries = 3 attempts; last failure returned.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, sequenceHandler.Attempts);
    }

    #endregion
}
