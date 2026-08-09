using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Chunking;

/// <summary>
/// Chunks text while respecting sentence boundaries to maintain semantic coherence.
/// </summary>
public class SentenceAwareChunkingStrategy : IChunkingStrategy
{
    #region Private-Members

    private readonly ChunkingOptions _options;
    private readonly ILogger<SentenceAwareChunkingStrategy>? _logger;
    // Matches a sentence as a run of non-terminator characters followed by one or more
    // terminators (. ! ?). The trailing whitespace is consumed but not captured into the
    // sentence so that punctuation is preserved while leading/trailing spaces are trimmed.
    private static readonly Regex SentenceRegex = new Regex(@"[^.!?]+[.!?]+|[^.!?]+$", RegexOptions.Compiled);

    #endregion

    /// <summary>
    /// Initializes a new instance of the SentenceAwareChunkingStrategy.
    /// </summary>
    /// <param name="options">The chunking options to use.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SentenceAwareChunkingStrategy(ChunkingOptions options, ILogger<SentenceAwareChunkingStrategy>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Splits a document into chunks while respecting sentence boundaries.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks.</returns>
    public Task<IReadOnlyList<IChunk>> ChunkAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Chunking document {DocumentId} with sentence-aware strategy (chunk size: {ChunkSize}, overlap: {OverlapSize})", 
            document.DocumentId, _options.ChunkSize, _options.OverlapSize);

        var chunks = new List<IChunk>();
        var text = document.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.LogWarning("Document {DocumentId} has empty content, returning no chunks", document.DocumentId);
            return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
        }

        // Segment into sentences while PRESERVING the terminal punctuation. Any sentence
        // longer than ChunkSize on its own is split into ChunkSize-bounded pieces so it
        // does not produce a single oversized chunk.
        var sentences = SentenceRegex.Matches(text)
            .Select(m => m.Value.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(SplitOversizedSentence)
            .ToList();

        _logger?.LogDebug("Document {DocumentId} split into {SentenceCount} sentences", document.DocumentId, sentences.Count);

        var currentChunk = new List<string>();
        int currentSize = 0;
        int chunkIndex = 0;
        int maxSentences = _options.MaxSentencesPerChunk ?? int.MaxValue;

        foreach (var sentence in sentences)
        {
            int sentenceSize = sentence.Length;
            var trimmedSentence = sentence.Trim();

            if (string.IsNullOrWhiteSpace(trimmedSentence))
                continue;

            bool shouldFinalize = (currentSize + sentenceSize > _options.ChunkSize && currentChunk.Count > 0) ||
                                  (currentChunk.Count >= maxSentences && currentChunk.Count > 0);

            if (shouldFinalize)
            {
                string chunkText = string.Join(" ", currentChunk).Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    chunks.Add(Chunk.Create(
                        chunkText,
                        chunkIndex++,
                        document.DocumentId,
                        new Dictionary<string, object>(document.Metadata)
                    ));
                }

                if (_options.OverlapSize > 0 && currentChunk.Count > 0)
                {
                    var overlapSentences = new List<string>();
                    int overlapSize = 0;
                    
                    for (int i = currentChunk.Count - 1; i >= 0; i--)
                    {
                        var overlapSentence = currentChunk[i];
                        int overlapSentenceSize = overlapSentence.Length + 1;
                        
                        if (overlapSentences.Count == 0)
                        {
                            overlapSentences.Insert(0, overlapSentence);
                            overlapSize += overlapSentenceSize;
                            continue;
                        }
                        
                        if (overlapSize + overlapSentenceSize > _options.OverlapSize * 1.2)
                            break;
                        
                        overlapSentences.Insert(0, overlapSentence);
                        overlapSize += overlapSentenceSize;
                        
                        if (overlapSize >= _options.OverlapSize * 0.8)
                            break;
                    }
                    
                    currentChunk = new List<string>(overlapSentences);
                    currentSize = overlapSize;
                }
                else
                {
                    currentChunk = new List<string>();
                    currentSize = 0;
                }
            }

            currentChunk.Add(trimmedSentence);
            currentSize += sentenceSize + 1;
        }

        if (currentChunk.Count > 0)
        {
            string chunkText = string.Join(" ", currentChunk).Trim();
            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(Chunk.Create(
                    chunkText,
                    chunkIndex,
                    document.DocumentId,
                    new Dictionary<string, object>(document.Metadata)
                ));
            }
        }

        _logger?.LogInformation("Document {DocumentId} split into {ChunkCount} chunks using sentence-aware strategy",
            document.DocumentId, chunks.Count);

        return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
    }

    #endregion

    #region Private-Methods

    /// <summary>
    /// Splits a single sentence that exceeds the configured chunk size into
    /// ChunkSize-bounded pieces. Sentences that fit are returned unchanged.
    /// </summary>
    /// <param name="sentence">The sentence to split if oversized.</param>
    /// <returns>One or more sentence fragments, none longer than ChunkSize.</returns>
    private IEnumerable<string> SplitOversizedSentence(string sentence)
    {
        int chunkSize = _options.ChunkSize;

        if (chunkSize <= 0 || sentence.Length <= chunkSize)
        {
            yield return sentence;
            yield break;
        }

        for (int start = 0; start < sentence.Length; start += chunkSize)
        {
            int length = Math.Min(chunkSize, sentence.Length - start);
            var piece = sentence.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(piece))
                yield return piece;
        }
    }

    #endregion
}
