using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Chunking;

/// <summary>
/// Chunks text using a sliding window approach with overlap.
/// </summary>
public class SlidingWindowChunkingStrategy : IChunkingStrategy
{
    #region Private-Members

    private readonly ChunkingOptions _options;
    private readonly ILogger<SlidingWindowChunkingStrategy>? _logger;

    #endregion

    /// <summary>
    /// Initializes a new instance of the SlidingWindowChunkingStrategy.
    /// </summary>
    /// <param name="options">The chunking options to use.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SlidingWindowChunkingStrategy(ChunkingOptions options, ILogger<SlidingWindowChunkingStrategy>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Splits a document into chunks using a sliding window with overlap.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks.</returns>
    public Task<IReadOnlyList<IChunk>> ChunkAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Chunking document {DocumentId} with sliding window strategy (chunk size: {ChunkSize}, overlap: {OverlapSize})", 
            document.DocumentId, _options.ChunkSize, _options.OverlapSize);

        var chunks = new List<IChunk>();
        var text = document.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.LogWarning("Document {DocumentId} has empty content, returning no chunks", document.DocumentId);
            return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
        }

        int step = _options.ChunkSize - _options.OverlapSize;
        if (step <= 0)
            step = _options.ChunkSize / 2;

        int chunkIndex = 0;
        for (int start = 0; start < text.Length; start += step)
        {
            int end = Math.Min(start + _options.ChunkSize, text.Length);
            string chunkText = text.Substring(start, end - start);

            chunks.Add(Chunk.Create(
                chunkText,
                chunkIndex++,
                document.DocumentId,
                new Dictionary<string, object>(document.Metadata)
            ));

            if (end >= text.Length)
                break;
        }

        _logger?.LogInformation("Document {DocumentId} split into {ChunkCount} chunks using sliding window strategy", 
            document.DocumentId, chunks.Count);

        return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
    }

    #endregion
}
