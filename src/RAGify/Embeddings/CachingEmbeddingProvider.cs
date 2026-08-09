using System.Security.Cryptography;
using System.Text;
using RAGify.Abstractions;

namespace RAGify.Embeddings;

/// <summary>
/// An <see cref="IEmbeddingProvider"/> decorator that caches embeddings produced by an inner
/// provider, avoiding re-computation (and re-payment) for identical inputs. Cache keys are a
/// content-derived SHA-256 hash, optionally scoped by a namespace.
/// </summary>
public class CachingEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    #region Private-Members

    private readonly IEmbeddingProvider _inner;
    private readonly IEmbeddingCache _cache;
    private readonly string? _cacheNamespace;

    #endregion

    #region Public-Members

    /// <summary>
    /// Gets the dimension of vectors produced by the inner provider.
    /// </summary>
    public int Dimension => _inner.Dimension;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingEmbeddingProvider"/> class.
    /// </summary>
    /// <param name="inner">The underlying embedding provider whose results are cached.</param>
    /// <param name="cache">The cache used to store and retrieve embeddings.</param>
    /// <param name="cacheNamespace">
    /// An optional namespace prefix mixed into every cache key. When <c>null</c>, the inner
    /// provider's type name is used so different providers do not collide.
    /// </param>
    public CachingEmbeddingProvider(IEmbeddingProvider inner, IEmbeddingCache cache, string? cacheNamespace = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cacheNamespace = cacheNamespace;
    }

    #region Public-Methods

    /// <summary>
    /// Generates an embedding for the specified text, returning a cached result when available.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the embedding vector.</returns>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var key = KeyFor(text);

        var cached = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached != null)
            return cached;

        var embedding = await _inner.EmbedAsync(text, cancellationToken).ConfigureAwait(false);
        await _cache.SetAsync(key, embedding, cancellationToken).ConfigureAwait(false);
        return embedding;
    }

    /// <summary>
    /// Generates embeddings for multiple texts, returning cached results where available and
    /// computing only the cache misses via the inner provider. Results preserve the original order.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of embedding vectors in the original order.</returns>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts == null || texts.Count == 0)
            return Array.Empty<float[]>();

        var results = new float[texts.Count][];
        var missKeys = new List<string>();
        var missTexts = new List<string>();
        var missIndices = new List<int>();

        for (int i = 0; i < texts.Count; i++)
        {
            var key = KeyFor(texts[i]);
            var cached = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);

            if (cached != null)
            {
                results[i] = cached;
            }
            else
            {
                missIndices.Add(i);
                missKeys.Add(key);
                missTexts.Add(texts[i]);
            }
        }

        if (missTexts.Count > 0)
        {
            var computed = await _inner.EmbedBatchAsync(missTexts, cancellationToken).ConfigureAwait(false);

            for (int j = 0; j < missIndices.Count; j++)
            {
                var embedding = computed[j];
                results[missIndices[j]] = embedding;
                await _cache.SetAsync(missKeys[j], embedding, cancellationToken).ConfigureAwait(false);
            }
        }

        return results;
    }

    /// <summary>
    /// Disposes the inner provider if it implements <see cref="IDisposable"/>.
    /// </summary>
    public void Dispose()
    {
        if (_inner is IDisposable d)
            d.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion

    #region Private-Methods

    /// <summary>
    /// Computes a stable, content-derived cache key (hex SHA-256) for the specified text,
    /// scoped by the configured namespace (or the inner provider's type name).
    /// </summary>
    /// <param name="text">The text to derive a key for.</param>
    /// <returns>An uppercase hex-encoded SHA-256 hash string.</returns>
    private string KeyFor(string text)
    {
        var prefix = _cacheNamespace ?? _inner.GetType().Name;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{prefix}{text}"));
        return Convert.ToHexString(hash);
    }

    #endregion
}
