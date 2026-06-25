using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using RAGify.Abstractions;

namespace RAGify.Tests;

/// <summary>
/// Deterministic, offline embedding provider used by the end-to-end integration tests.
/// It maps text to a stable, non-negative bag-of-words pseudo-vector: each lowercased word is
/// hashed into one of <see cref="Dimension"/> buckets and contributes a positive weight. Because
/// all components are non-negative, cosine similarity is always &gt;= 0, texts that share words
/// produce high cosine similarity, and texts with disjoint vocabularies stay near-orthogonal.
/// A small base value is added to every component so empty/short inputs never yield a zero vector.
/// </summary>
/// <remarks>
/// The provider records, per exact input text, how many times the inner embedding routine actually
/// ran. This lets caching tests assert cache hits vs. misses on a specific query string.
/// </remarks>
internal sealed class IntegrationHashEmbeddingProvider : IEmbeddingProvider
{
    private readonly ConcurrentDictionary<string, int> _embedCounts = new(StringComparer.Ordinal);

    public IntegrationHashEmbeddingProvider(int dimension = 64)
    {
        if (dimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimension));

        Dimension = dimension;
    }

    /// <summary>
    /// Gets the (small) embedding dimensionality. A modest dimensionality (default 64) keeps the
    /// vectors tiny while leaving enough hash buckets to avoid spurious collisions, so distinct
    /// topics stay well separated and same-topic text clears the retrieval similarity threshold.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    /// Gets the number of times the underlying embedding computation actually ran for the exact
    /// input text. Used by caching tests to distinguish cache hits from misses.
    /// </summary>
    public int EmbedCountFor(string text) => _embedCounts.TryGetValue(text, out var c) ? c : 0;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Compute(text));
    }

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
            vectors[i] = Compute(texts[i]);

        return Task.FromResult<IReadOnlyList<float[]>>(vectors);
    }

    private float[] Compute(string text)
    {
        // Record that the inner computation ran for this exact input.
        _embedCounts.AddOrUpdate(text ?? string.Empty, 1, (_, prev) => prev + 1);

        // Small positive base keeps the vector non-zero and bounds the worst-case similarity floor.
        var vector = new float[Dimension];
        for (var i = 0; i < Dimension; i++)
            vector[i] = 0.01f;

        if (string.IsNullOrWhiteSpace(text))
            return vector;

        foreach (var word in Tokenize(text))
        {
            var bucket = (int)(StableHash(word) % (uint)Dimension);
            vector[bucket] += 1.0f;
        }

        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (var raw in text.ToLowerInvariant()
                     .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '"', '\'', '-', '/' },
                         StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length > 0)
                yield return raw;
        }
    }

    // Deterministic FNV-1a hash so results are stable across runs and platforms.
    private static uint StableHash(string value)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;

        var hash = fnvOffset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= fnvPrime;
        }

        return hash;
    }
}

/// <summary>
/// Deterministic, offline chat-completion provider used by the end-to-end integration tests.
/// Its answer echoes the user message and reports how many numbered context markers (e.g. <c>[1]</c>)
/// were present in the prompt, allowing tests to assert that retrieved context was actually passed
/// into the prompt that the LLM received.
/// </summary>
internal sealed class EchoLlmProvider : ILlmProvider
{
    public string Model => "echo";

    /// <summary>Gets the messages from the most recent <see cref="CompleteAsync"/>/<see cref="StreamAsync"/> call.</summary>
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public Task<ChatCompletion> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastMessages = messages;

        var content = BuildContent(messages);
        return Task.FromResult(new ChatCompletion
        {
            Content = content,
            Model = Model,
            PromptTokens = EstimateTokens(messages),
            CompletionTokens = EstimateTokens(content),
            FinishReason = "stop"
        });
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastMessages = messages;

        var content = BuildContent(messages);

        // Yield the content in a few fragments to exercise the streaming path.
        foreach (var fragment in SplitIntoFragments(content, 3))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return fragment;
            await Task.Yield();
        }
    }

    private static string BuildContent(IReadOnlyList<ChatMessage> messages)
    {
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? string.Empty;

        // Count numbered context markers (e.g. "[1]") present anywhere in the prompt so a test can
        // assert that retrieved context made it into the messages handed to the LLM.
        var contextMarkers = messages
            .Sum(m => System.Text.RegularExpressions.Regex.Matches(m.Content, @"\[\d+\]").Count);

        var truncated = lastUser.Length > 120 ? lastUser[..120] : lastUser;
        return $"ANSWER: {truncated} (context_markers={contextMarkers})";
    }

    private static IEnumerable<string> SplitIntoFragments(string content, int parts)
    {
        if (string.IsNullOrEmpty(content) || parts <= 1)
        {
            yield return content;
            yield break;
        }

        var size = (int)Math.Ceiling(content.Length / (double)parts);
        for (var start = 0; start < content.Length; start += size)
        {
            var length = Math.Min(size, content.Length - start);
            yield return content.Substring(start, length);
        }
    }

    private static int EstimateTokens(IReadOnlyList<ChatMessage> messages) =>
        messages.Sum(m => EstimateTokens(m.Content));

    private static int EstimateTokens(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, text.Length / 4);
}
