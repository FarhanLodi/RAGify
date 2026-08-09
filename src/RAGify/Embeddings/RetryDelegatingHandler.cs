using System.Net;

namespace RAGify.Embeddings;

/// <summary>
/// A <see cref="DelegatingHandler"/> that transparently retries transient HTTP failures
/// (HTTP 429 and 5xx responses, and <see cref="HttpRequestException"/>) using exponential
/// backoff. A <c>Retry-After</c> response header, when present, takes precedence over the
/// computed backoff delay.
/// </summary>
/// <remarks>
/// This handler resends the same <see cref="HttpRequestMessage"/> instance on retry. It is
/// intended for idempotent requests (such as the JSON POSTs used by RAGify embedding and chat
/// providers) whose content can be safely re-sent. Intermediate failed responses are disposed
/// before each retry.
/// </remarks>
public class RetryDelegatingHandler : DelegatingHandler
{
    #region Private-Members

    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryDelegatingHandler"/> class.
    /// </summary>
    /// <param name="maxRetries">The maximum number of retry attempts after the initial request (default: 3).</param>
    /// <param name="baseDelay">The base delay for exponential backoff. Defaults to 500ms when <c>null</c>.</param>
    public RetryDelegatingHandler(int maxRetries = 3, TimeSpan? baseDelay = null)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "maxRetries cannot be negative.");

        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(500);
    }

    #region Protected-Methods

    /// <summary>
    /// Sends the request, retrying transient failures up to the configured maximum.
    /// </summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the final response (success or last failure).</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;

        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (!IsTransient(response.StatusCode) || attempt >= _maxRetries)
                    return response;

                var delay = GetRetryAfterDelay(response) ?? ComputeBackoff(attempt);

                // Dispose the failed response before retrying to free its connection/content.
                response.Dispose();
                response = null;

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
                response?.Dispose();
                response = null;

                await Task.Delay(ComputeBackoff(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    #endregion

    #region Private-Methods

    /// <summary>
    /// Determines whether the specified status code represents a transient, retryable failure.
    /// </summary>
    /// <param name="statusCode">The HTTP status code to evaluate.</param>
    /// <returns><c>true</c> if the status is HTTP 429 or any 5xx; otherwise <c>false</c>.</returns>
    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
    }

    /// <summary>
    /// Computes the exponential backoff delay for the given attempt (<c>baseDelay * 2^attempt</c>).
    /// </summary>
    /// <param name="attempt">The zero-based attempt index.</param>
    /// <returns>The computed delay.</returns>
    private TimeSpan ComputeBackoff(int attempt)
    {
        return TimeSpan.FromTicks(_baseDelay.Ticks * (1L << attempt));
    }

    /// <summary>
    /// Extracts a delay from the response's <c>Retry-After</c> header, supporting both a
    /// delta-in-seconds and an HTTP-date value.
    /// </summary>
    /// <param name="response">The HTTP response to inspect.</param>
    /// <returns>The honored delay, or <c>null</c> when no usable <c>Retry-After</c> header is present.</returns>
    private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter == null)
            return null;

        if (retryAfter.Delta.HasValue)
            return retryAfter.Delta.Value > TimeSpan.Zero ? retryAfter.Delta.Value : TimeSpan.Zero;

        if (retryAfter.Date.HasValue)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    #endregion
}
