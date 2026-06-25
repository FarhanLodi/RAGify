using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RAGify.Abstractions;

namespace RAGify.Reranking;

/// <summary>
/// Dependency-free lexical reranker implementing BM25 scoring. Useful as a local,
/// offline fallback when no external rerank service is available.
/// </summary>
public class LexicalReranker : IReranker
{
    #region Private-Members

    private const double K1 = 1.5;
    private const double B = 0.75;

    private static readonly Regex TokenSplitter = new("[^a-z0-9]+", RegexOptions.Compiled);

    private readonly ILogger<LexicalReranker>? _logger;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="LexicalReranker"/> class.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public LexicalReranker(ILogger<LexicalReranker>? logger = null)
    {
        _logger = logger;
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Reranks the supplied documents against the query using BM25 lexical scoring.
    /// </summary>
    /// <param name="query">The query to score documents against.</param>
    /// <param name="documents">The candidate documents to rerank.</param>
    /// <param name="topK">The maximum number of results to return.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The top <paramref name="topK"/> results ordered by descending BM25 score.</returns>
    public Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (documents == null || documents.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RerankResult>>(Array.Empty<RerankResult>());
        }

        cancellationToken.ThrowIfCancellationRequested();

        var queryTerms = Tokenize(query);

        // Tokenize each document once, computing term frequencies and lengths.
        var docCount = documents.Count;
        var termFrequencies = new Dictionary<string, int>[docCount];
        var docLengths = new int[docCount];
        long totalLength = 0;

        for (var i = 0; i < docCount; i++)
        {
            var tokens = Tokenize(documents[i]);
            docLengths[i] = tokens.Count;
            totalLength += tokens.Count;

            var tf = new Dictionary<string, int>();
            foreach (var token in tokens)
            {
                tf.TryGetValue(token, out var count);
                tf[token] = count + 1;
            }

            termFrequencies[i] = tf;
        }

        var avgDocLength = docCount > 0 ? (double)totalLength / docCount : 0.0;

        // Document frequency per distinct query term.
        var distinctQueryTerms = new HashSet<string>(queryTerms);
        var documentFrequency = new Dictionary<string, int>();
        foreach (var term in distinctQueryTerms)
        {
            var df = 0;
            for (var i = 0; i < docCount; i++)
            {
                if (termFrequencies[i].ContainsKey(term))
                {
                    df++;
                }
            }

            documentFrequency[term] = df;
        }

        // Precompute IDF per distinct query term.
        var idf = new Dictionary<string, double>();
        foreach (var term in distinctQueryTerms)
        {
            var df = documentFrequency[term];
            idf[term] = Math.Log(((docCount - df + 0.5) / (df + 0.5)) + 1.0);
        }

        var scored = new List<RerankResult>(docCount);
        for (var i = 0; i < docCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var score = 0.0;
            var docLen = docLengths[i];
            var tf = termFrequencies[i];

            foreach (var term in distinctQueryTerms)
            {
                if (!tf.TryGetValue(term, out var termFreq) || termFreq == 0)
                {
                    continue;
                }

                var denominator = termFreq + (K1 * (1.0 - B + (B * (avgDocLength > 0 ? docLen / avgDocLength : 0.0))));
                score += idf[term] * ((termFreq * (K1 + 1.0)) / denominator);
            }

            scored.Add(new RerankResult
            {
                Index = i,
                Score = score,
                Document = documents[i]
            });
        }

        // Deterministic ordering: descending score, then ascending index as a tiebreaker.
        var ordered = scored
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Index)
            .Take(Math.Max(0, topK))
            .ToList();

        _logger?.LogDebug(
            "Lexically reranked {DocumentCount} documents; returning {ResultCount}.",
            docCount, ordered.Count);

        return Task.FromResult<IReadOnlyList<RerankResult>>(ordered);
    }

    #endregion

    #region Private-Methods

    private static List<string> Tokenize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new List<string>();
        }

        var parts = TokenSplitter.Split(text.ToLowerInvariant());
        var tokens = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                tokens.Add(part);
            }
        }

        return tokens;
    }

    #endregion
}
