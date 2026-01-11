using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Chunking;

/// <summary>
/// Chunks text into fixed-size pieces with optional overlap.
/// </summary>
public class FixedSizeChunkingStrategy : IChunkingStrategy
{
    #region Private-Members

    private readonly ChunkingOptions _options;
    private readonly ILogger<FixedSizeChunkingStrategy>? _logger;

    #endregion

    /// <summary>
    /// Initializes a new instance of the FixedSizeChunkingStrategy.
    /// </summary>
    /// <param name="options">The chunking options to use.</param>
    /// <param name="logger">Optional logger instance.</param>
    public FixedSizeChunkingStrategy(ChunkingOptions options, ILogger<FixedSizeChunkingStrategy>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Splits a document into fixed-size chunks with overlap.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks.</returns>
    public Task<IReadOnlyList<IChunk>> ChunkAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Chunking document {DocumentId} with fixed-size strategy (chunk size: {ChunkSize}, overlap: {OverlapSize})", 
            document.DocumentId, _options.ChunkSize, _options.OverlapSize);

        var chunks = new List<IChunk>();
        var text = document.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.LogWarning("Document {DocumentId} has empty content, returning no chunks", document.DocumentId);
            return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
        }

        int start = 0;
        int chunkIndex = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + _options.ChunkSize, text.Length);
            string chunkText = text.Substring(start, end - start);

            var chunk = Chunk.Create(
                chunkText,
                chunkIndex,
                document.DocumentId,
                new Dictionary<string, object>(document.Metadata)
            );

            chunks.Add(chunk);

            start = end - _options.OverlapSize;
            if (start >= end) start = end;
            chunkIndex++;
        }

        _logger?.LogInformation("Document {DocumentId} split into {ChunkCount} chunks using fixed-size strategy", 
            document.DocumentId, chunks.Count);

        return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
    }

    #endregion
}
