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
    private static readonly Regex SentenceEndRegex = new Regex(@"[.!?]+\s+", RegexOptions.Compiled);

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

        var sentences = SentenceEndRegex.Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
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
            string chunkText = string.Join(" ", currentChunk);
            chunks.Add(Chunk.Create(
                chunkText,
                chunkIndex,
                document.DocumentId,
                new Dictionary<string, object>(document.Metadata)
            ));
        }

        _logger?.LogInformation("Document {DocumentId} split into {ChunkCount} chunks using sentence-aware strategy", 
            document.DocumentId, chunks.Count);

        return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
    }

    #endregion
}
