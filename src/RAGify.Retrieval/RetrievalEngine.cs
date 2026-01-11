using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RAGify.Abstractions;

namespace RAGify.Retrieval;

/// <summary>
/// Default implementation of IRetrievalEngine that provides semantic search with intelligent filtering and deduplication.
/// </summary>
public class RetrievalEngine : IRetrievalEngine
{
    #region Private-Members

    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<RetrievalEngine>? _logger;
    private readonly Dictionary<string, IChunk> _chunkCache = new();
    private static readonly Regex FactQuestionPattern = new Regex(
        @"\b(what|who|when|where|which|how many|how much)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplanatoryQuestionPattern = new Regex(
        @"\b(how|why|explain|describe|tell me about|what is|what are)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    #endregion

    public RetrievalEngine(IEmbeddingProvider embeddingProvider, IVectorStore vectorStore, ILogger<RetrievalEngine>? logger = null)
    {
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Registers a chunk for retrieval. This should be called when chunks are indexed.
    /// </summary>
    /// <param name="chunk">The chunk to register.</param>
    public void RegisterChunk(IChunk chunk)
    {
        _chunkCache[chunk.ChunkId] = chunk;
        _logger?.LogDebug("Registered chunk {ChunkId} for document {DocumentId}", chunk.ChunkId, chunk.DocumentId);
    }

    /// <summary>
    /// Registers multiple chunks for retrieval.
    /// </summary>
    /// <param name="chunks">The chunks to register.</param>
    public void RegisterChunks(IEnumerable<IChunk> chunks)
    {
        var chunkList = chunks.ToList();
        foreach (var chunk in chunkList)
        {
            RegisterChunk(chunk);
        }
        _logger?.LogDebug("Registered {ChunkCount} chunks", chunkList.Count);
    }

    /// <summary>
    /// Retrieves relevant chunks for a given query using semantic search.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="options">Optional retrieval options for customizing search behavior.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of retrieval results with similarity scores.</returns>
    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        RetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Retrieving results for query: {Query}", query);
        options ??= new RetrievalOptions();

        var questionType = DetectQuestionType(query);
        var effectiveTopK = options.EnableDynamicTopK && options.TopK == 0
            ? DetermineTopK(query, questionType, 3)
            : (options.TopK > 0 ? options.TopK : 3);
        
        var effectiveThreshold = options.SimilarityThreshold > 0.0 
            ? options.SimilarityThreshold 
            : 0.35;

        _logger?.LogDebug("Question type: {QuestionType}, Effective TopK: {TopK}, Threshold: {Threshold}", 
            questionType, effectiveTopK, effectiveThreshold);

        var queryEmbedding = await _embeddingProvider.EmbedAsync(query, cancellationToken);
        _logger?.LogDebug("Generated query embedding with dimension {Dimension}", queryEmbedding.Length);

        var searchTopK = Math.Max(effectiveTopK * 3, 15);
        var searchResults = await _vectorStore.SearchAsync(
            queryEmbedding,
            searchTopK,
            effectiveThreshold,
            options.Filter,
            cancellationToken);

        _logger?.LogDebug("Vector store returned {ResultCount} search results", searchResults.Count);

        var retrievalResults = new List<RetrievalResult>();

        foreach (var searchResult in searchResults)
        {
            if (_chunkCache.TryGetValue(searchResult.VectorId, out var chunk))
            {
                retrievalResults.Add(new RetrievalResult
                {
                    Chunk = chunk,
                    Similarity = searchResult.Similarity
                });
            }
        }

        var chunksBeforeDeduplication = retrievalResults.Count;
        _logger?.LogDebug("Found {ChunkCount} chunks in cache before filtering", chunksBeforeDeduplication);

        var filteredResults = FilterLowValueChunks(retrievalResults, effectiveThreshold);
        _logger?.LogDebug("Filtered to {FilteredCount} chunks after low-value filtering", filteredResults.Count);

        var finalResults = options.EnableDeduplication
            ? DeduplicateResults(filteredResults, effectiveTopK)
            : filteredResults.Take(effectiveTopK).ToList();

        _logger?.LogInformation("Retrieved {FinalCount} results for query (deduplication: {DeduplicationEnabled})", 
            finalResults.Count, options.EnableDeduplication);

        return finalResults;
    }

    /// <summary>
    /// Retrieves chunks with metadata about the retrieval process.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="options">Optional retrieval options for customizing search behavior.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a tuple with the retrieved results and metadata about the retrieval process.</returns>
    public async Task<(IReadOnlyList<RetrievalResult> Results, RetrievalMetadata Metadata)> RetrieveWithMetadataAsync(
        string query,
        RetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Retrieving results with metadata for query: {Query}", query);
        options ??= new RetrievalOptions();

        var questionType = DetectQuestionType(query);
        var effectiveTopK = options.EnableDynamicTopK && options.TopK == 0
            ? DetermineTopK(query, questionType, 3)
            : (options.TopK > 0 ? options.TopK : 3);
        
        var effectiveThreshold = options.SimilarityThreshold > 0.0 
            ? options.SimilarityThreshold 
            : 0.35;

        _logger?.LogDebug("Question type: {QuestionType}, Effective TopK: {TopK}, Threshold: {Threshold}", 
            questionType, effectiveTopK, effectiveThreshold);

        var queryEmbedding = await _embeddingProvider.EmbedAsync(query, cancellationToken);
        _logger?.LogDebug("Generated query embedding with dimension {Dimension}", queryEmbedding.Length);

        var searchTopK = Math.Max(effectiveTopK * 3, 15);
        var searchResults = await _vectorStore.SearchAsync(
            queryEmbedding,
            searchTopK,
            effectiveThreshold,
            options.Filter,
            cancellationToken);

        _logger?.LogDebug("Vector store returned {ResultCount} search results", searchResults.Count);

        var retrievalResults = new List<RetrievalResult>();

        foreach (var searchResult in searchResults)
        {
            if (_chunkCache.TryGetValue(searchResult.VectorId, out var chunk))
            {
                retrievalResults.Add(new RetrievalResult
                {
                    Chunk = chunk,
                    Similarity = searchResult.Similarity
                });
            }
        }

        var chunksBeforeDeduplication = retrievalResults.Count;
        _logger?.LogDebug("Found {ChunkCount} chunks in cache before filtering", chunksBeforeDeduplication);

        var filteredResults = FilterLowValueChunks(retrievalResults, effectiveThreshold);
        _logger?.LogDebug("Filtered to {FilteredCount} chunks after low-value filtering", filteredResults.Count);

        var finalResults = options.EnableDeduplication
            ? DeduplicateResults(filteredResults, effectiveTopK)
            : filteredResults.Take(effectiveTopK).ToList();

        var metadata = new RetrievalMetadata
        {
            EffectiveTopK = effectiveTopK,
            SimilarityThreshold = effectiveThreshold,
            QuestionType = questionType,
            ChunksBeforeDeduplication = chunksBeforeDeduplication,
            DynamicTopKUsed = options.EnableDynamicTopK && options.TopK == 0,
            DeduplicationApplied = options.EnableDeduplication
        };

        _logger?.LogInformation("Retrieved {FinalCount} results with metadata (deduplication: {DeduplicationEnabled})", 
            finalResults.Count, options.EnableDeduplication);

        return (finalResults, metadata);
    }

    #endregion

    #region Private-Methods

    private static string DetectQuestionType(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        if (FactQuestionPattern.IsMatch(query))
        {
            return "Fact";
        }

        if (ExplanatoryQuestionPattern.IsMatch(query))
        {
            return "Explanatory";
        }

        if (Regex.IsMatch(query, @"\b(list|name|enumerate|all|examples?)\b", RegexOptions.IgnoreCase))
        {
            return "List";
        }

        return "General";
    }

    private static int DetermineTopK(string query, string questionType, int defaultTopK)
    {
        switch (questionType)
        {
            case "Fact":
                return 2;
            
            case "Explanatory":
                return 5;
            
            case "List":
                return 4;
            
            default:
                return defaultTopK > 0 ? defaultTopK : 3;
        }
    }

    private List<RetrievalResult> FilterLowValueChunks(
        List<RetrievalResult> results,
        double threshold)
    {
        if (results.Count == 0)
            return results;

        var filtered = new List<RetrievalResult>();

        foreach (var result in results)
        {
            if (result.Chunk.Text.Trim().Length < 20)
                continue;

            if (result.Similarity < threshold)
                continue;

            var textWithoutWhitespace = System.Text.RegularExpressions.Regex.Replace(
                result.Chunk.Text, @"\s+", "");
            if (textWithoutWhitespace.Length < 10)
                continue;

            filtered.Add(result);
        }

        return filtered;
    }

    private static IReadOnlyList<RetrievalResult> DeduplicateResults(
        IReadOnlyList<RetrievalResult> results,
        int maxResults)
    {
        if (results.Count == 0)
            return results;

        var deduplicated = new List<RetrievalResult>();
        var seenTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSemanticFingerprints = new List<string>();

        var sortedResults = results.OrderByDescending(r => r.Similarity).ToList();

        foreach (var result in sortedResults)
        {
            var chunkText = result.Chunk.Text.Trim();
            
            if (IsDuplicate(chunkText, seenTexts, seenSemanticFingerprints))
                continue;

            var normalizedText = NormalizeForComparison(chunkText);
            seenTexts.Add(normalizedText);

            var semanticFingerprint = CreateSemanticFingerprint(chunkText);
            seenSemanticFingerprints.Add(semanticFingerprint);

            deduplicated.Add(result);

            if (deduplicated.Count >= maxResults)
                break;
        }

        return deduplicated;
    }

    private static bool IsDuplicate(string text, HashSet<string> seenTexts, List<string> seenSemanticFingerprints)
    {
        var normalized = NormalizeForComparison(text);

        if (seenTexts.Contains(normalized))
            return true;

        foreach (var seen in seenTexts)
        {
            var overlap = CalculateTextOverlap(normalized, seen);
            if (overlap > 0.75)
                return true;
        }

        var fingerprint = CreateSemanticFingerprint(text);
        foreach (var seenFingerprint in seenSemanticFingerprints)
        {
            var fingerprintOverlap = CalculateTextOverlap(fingerprint, seenFingerprint);
            if (fingerprintOverlap > 0.7)
                return true;
        }

        return false;
    }

    private static string CreateSemanticFingerprint(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
            "by", "from", "as", "is", "was", "are", "were", "been", "be", "have", "has", "had",
            "do", "does", "did", "will", "would", "could", "should", "may", "might", "must",
            "this", "that", "these", "those", "it", "its", "they", "them", "their", "there",
            "what", "which", "who", "whom", "whose", "where", "when", "why", "how"
        };

        var words = System.Text.RegularExpressions.Regex
            .Split(text.ToLowerInvariant(), @"\W+")
            .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length > 2 && !stopWords.Contains(w))
            .OrderBy(w => w)
            .Take(10)
            .ToList();

        return string.Join(" ", words);
    }

    private static string NormalizeForComparison(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            text.ToLowerInvariant(),
            @"\s+",
            " ").Trim();
    }

    private static double CalculateTextOverlap(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0.0;

        var words1 = text1.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var words2 = text2.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (words1.Length == 0 || words2.Length == 0)
            return 0.0;

        var set1 = new HashSet<string>(words1, StringComparer.OrdinalIgnoreCase);
        var set2 = new HashSet<string>(words2, StringComparer.OrdinalIgnoreCase);

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    #endregion
}
