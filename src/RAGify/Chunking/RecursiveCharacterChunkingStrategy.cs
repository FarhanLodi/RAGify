using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Chunking;

/// <summary>
/// Chunks text by recursively splitting it on an ordered list of separators, greedily
/// merging adjacent pieces so that each emitted chunk is as close as possible to (but does
/// not exceed) the configured chunk size. Modeled after LangChain's RecursiveCharacterTextSplitter.
/// </summary>
public class RecursiveCharacterChunkingStrategy : IChunkingStrategy
{
    #region Private-Members

    private readonly ChunkingOptions _options;
    private readonly ILogger<RecursiveCharacterChunkingStrategy>? _logger;

    private static readonly string[] Separators = { "\n\n", "\n", ". ", " ", "" };

    #endregion

    /// <summary>
    /// Initializes a new instance of the RecursiveCharacterChunkingStrategy.
    /// </summary>
    /// <param name="options">The chunking options to use.</param>
    /// <param name="logger">Optional logger instance.</param>
    public RecursiveCharacterChunkingStrategy(ChunkingOptions options, ILogger<RecursiveCharacterChunkingStrategy>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Splits a document into chunks by recursively applying an ordered separator list.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks.</returns>
    public Task<IReadOnlyList<IChunk>> ChunkAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Chunking document {DocumentId} with recursive character strategy (chunk size: {ChunkSize}, overlap: {OverlapSize})",
            document.DocumentId, _options.ChunkSize, _options.OverlapSize);

        var chunks = new List<IChunk>();
        var text = document.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.LogWarning("Document {DocumentId} has empty content, returning no chunks", document.DocumentId);
            return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
        }

        int chunkSize = _options.ChunkSize > 0 ? _options.ChunkSize : 1;

        // Clamp overlap so it can never reach or exceed the chunk size (which would prevent termination).
        int overlap = _options.OverlapSize;
        if (overlap >= chunkSize)
            overlap = chunkSize / 2;
        if (overlap < 0)
            overlap = 0;

        // Recursively split into pieces no larger than the chunk size, then merge greedily.
        var pieces = SplitRecursive(text, 0, chunkSize);
        var merged = MergePieces(pieces, chunkSize);

        int chunkIndex = 0;
        string? previous = null;

        foreach (var piece in merged)
        {
            var chunkText = piece;

            // Carry the trailing overlap characters of the previous chunk onto this one.
            if (overlap > 0 && previous != null)
            {
                int take = Math.Min(overlap, previous.Length);
                var carry = previous.Substring(previous.Length - take);
                chunkText = carry + chunkText;
            }

            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(Chunk.Create(
                    chunkText,
                    chunkIndex++,
                    document.DocumentId,
                    new Dictionary<string, object>(document.Metadata)
                ));

                previous = piece;
            }
        }

        _logger?.LogInformation("Document {DocumentId} split into {ChunkCount} chunks using recursive character strategy",
            document.DocumentId, chunks.Count);

        return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
    }

    #endregion

    #region Private-Methods

    /// <summary>
    /// Recursively splits text on the separator at the given level, descending to the next
    /// separator for any piece that still exceeds the chunk size.
    /// </summary>
    private static List<string> SplitRecursive(string text, int separatorIndex, int chunkSize)
    {
        var result = new List<string>();

        if (string.IsNullOrEmpty(text))
            return result;

        if (text.Length <= chunkSize)
        {
            result.Add(text);
            return result;
        }

        // No separators left: hard-split by character window.
        if (separatorIndex >= Separators.Length)
        {
            for (int i = 0; i < text.Length; i += chunkSize)
                result.Add(text.Substring(i, Math.Min(chunkSize, text.Length - i)));
            return result;
        }

        var separator = Separators[separatorIndex];

        // Empty separator is the final fallback: split by character window.
        if (separator.Length == 0)
        {
            for (int i = 0; i < text.Length; i += chunkSize)
                result.Add(text.Substring(i, Math.Min(chunkSize, text.Length - i)));
            return result;
        }

        var parts = text.Split(new[] { separator }, StringSplitOptions.None);

        foreach (var part in parts)
        {
            if (part.Length == 0)
                continue;

            if (part.Length <= chunkSize)
            {
                result.Add(part);
            }
            else
            {
                // Piece is still too large; recurse with the next separator.
                result.AddRange(SplitRecursive(part, separatorIndex + 1, chunkSize));
            }
        }

        return result;
    }

    /// <summary>
    /// Greedily merges adjacent pieces back together so each emitted chunk is as close to
    /// (but not exceeding) the chunk size as possible.
    /// </summary>
    private static List<string> MergePieces(List<string> pieces, int chunkSize)
    {
        var merged = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var piece in pieces)
        {
            if (string.IsNullOrEmpty(piece))
                continue;

            // A single oversized piece cannot be merged; emit it on its own.
            if (piece.Length >= chunkSize)
            {
                if (current.Length > 0)
                {
                    merged.Add(current.ToString());
                    current.Clear();
                }
                merged.Add(piece);
                continue;
            }

            int separatorLength = current.Length > 0 ? 1 : 0;
            if (current.Length + separatorLength + piece.Length > chunkSize && current.Length > 0)
            {
                merged.Add(current.ToString());
                current.Clear();
                separatorLength = 0;
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(piece);
        }

        if (current.Length > 0)
            merged.Add(current.ToString());

        return merged;
    }

    #endregion
}
