namespace RAGify.Embeddings;

/// <summary>
/// Factory for creating <see cref="HttpClient"/> instances that automatically retry transient
/// HTTP failures with exponential backoff via a <see cref="RetryDelegatingHandler"/>.
/// </summary>
public static class ResilientHttpClientFactory
{
    #region Public-Methods

    /// <summary>
    /// Creates an <see cref="HttpClient"/> backed by a <see cref="RetryDelegatingHandler"/> for
    /// automatic retry/backoff on transient failures (HTTP 429, 5xx, and connection errors).
    /// </summary>
    /// <param name="maxRetries">The maximum number of retry attempts after the initial request (default: 3).</param>
    /// <param name="baseDelay">The base delay for exponential backoff. Defaults to 500ms when <c>null</c>.</param>
    /// <returns>A configured <see cref="HttpClient"/>.</returns>
    /// <remarks>
    /// The returned <see cref="HttpClient"/> can be passed into any RAGify embedding or chat
    /// provider's <c>httpClient</c> parameter to give that provider automatic retry/backoff
    /// behavior without any additional configuration.
    /// </remarks>
    public static HttpClient Create(int maxRetries = 3, TimeSpan? baseDelay = null)
    {
        return new HttpClient(new RetryDelegatingHandler(maxRetries, baseDelay)
        {
            InnerHandler = new HttpClientHandler()
        });
    }

    #endregion
}
