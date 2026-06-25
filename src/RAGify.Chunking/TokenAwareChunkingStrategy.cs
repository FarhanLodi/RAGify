using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Chunking;

/// <summary>
/// Chunks text by an estimated token budget rather than by characters, honoring the
/// <see cref="ChunkingOptions.RespectTokenBoundaries"/> intent. <see cref="ChunkingOptions.ChunkSize"/>
/// is treated as the target token budget per chunk and <see cref="ChunkingOptions.OverlapSize"/>
/// as the token overlap. By default a dependency-free heuristic estimates ~4 characters per token;
/// a custom <see cref="TokenCounter"/> may be supplied to use a real tokenizer.
/// </summary>
public class TokenAwareChunkingStrategy : IChunkingStrategy
{
    #region Public-Members

    /// <summary>
    /// Gets or sets an optional token counter. When set, it is used instead of the built-in
    /// heuristic to estimate the token count of a piece of text. Default is null.
    /// </summary>
    public Func<string, int>? TokenCounter { get; set; }

    #endregion

    #region Private-Members

    private readonly ChunkingOptions _options;
    private readonly ILogger<TokenAwareChunkingStrategy>? _logger;

    #endregion

    /// <summary>
    /// Initializes a new instance of the TokenAwareChunkingStrategy.
    /// </summary>
    /// <param name="options">The chunking options to use.</param>
    /// <param name="logger">Optional logger instance.</param>
    public TokenAwareChunkingStrategy(ChunkingOptions options, ILogger<TokenAwareChunkingStrategy>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Splits a document into chunks sized by an estimated token budget.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks.</returns>
    public Task<IReadOnlyList<IChunk>> ChunkAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Chunking document {DocumentId} with token-aware strategy (token budget: {ChunkSize}, overlap: {OverlapSize})",
            document.DocumentId, _options.ChunkSize, _options.OverlapSize);

        var chunks = new List<IChunk>();
        var text = document.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.LogWarning("Document {DocumentId} has empty content, returning no chunks", document.DocumentId);
            return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
        }

        int budget = _options.ChunkSize > 0 ? _options.ChunkSize : 1;

        // Clamp overlap so it can never reach or exceed the budget (which would prevent termination).
        int overlap = _options.OverlapSize;
        if (overlap >= budget)
            overlap = budget / 2;
        if (overlap < 0)
            overlap = 0;

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var currentWords = new List<string>();
        var currentTokens = new List<int>();
        int currentTotal = 0;
        int chunkIndex = 0;

        foreach (var word in words)
        {
            int wordTokens = CountTokens(word);

            // If a single word exceeds the budget, emit any pending chunk then emit the word alone.
            if (wordTokens >= budget)
            {
                if (currentWords.Count > 0)
                {
                    EmitChunk(chunks, currentWords, ref chunkIndex, document);
                    currentWords.Clear();
                    currentTokens.Clear();
                    currentTotal = 0;
                }

                EmitChunk(chunks, new List<string> { word }, ref chunkIndex, document);
                continue;
            }

            // Adding this word would exceed the budget: finalize the current chunk first.
            if (currentTotal + wordTokens > budget && currentWords.Count > 0)
            {
                EmitChunk(chunks, currentWords, ref chunkIndex, document);

                // Seed the next chunk with the trailing words whose cumulative tokens ~= overlap.
                if (overlap > 0)
                {
                    var carryWords = new List<string>();
                    var carryTokens = new List<int>();
                    int carryTotal = 0;

                    for (int i = currentWords.Count - 1; i >= 0; i--)
                    {
                        if (carryTotal + currentTokens[i] > overlap && carryWords.Count > 0)
                            break;

                        carryWords.Insert(0, currentWords[i]);
                        carryTokens.Insert(0, currentTokens[i]);
                        carryTotal += currentTokens[i];

                        if (carryTotal >= overlap)
                            break;
                    }

                    currentWords = carryWords;
                    currentTokens = carryTokens;
                    currentTotal = carryTotal;
                }
                else
                {
                    currentWords = new List<string>();
                    currentTokens = new List<int>();
                    currentTotal = 0;
                }
            }

            currentWords.Add(word);
            currentTokens.Add(wordTokens);
            currentTotal += wordTokens;
        }

        if (currentWords.Count > 0)
            EmitChunk(chunks, currentWords, ref chunkIndex, document);

        _logger?.LogInformation("Document {DocumentId} split into {ChunkCount} chunks using token-aware strategy",
            document.DocumentId, chunks.Count);

        return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
    }

    #endregion

    #region Private-Methods

    private void EmitChunk(List<IChunk> chunks, List<string> words, ref int chunkIndex, IDocument document)
    {
        var chunkText = string.Join(" ", words).Trim();
        if (string.IsNullOrWhiteSpace(chunkText))
            return;

        chunks.Add(Chunk.Create(
            chunkText,
            chunkIndex++,
            document.DocumentId,
            new Dictionary<string, object>(document.Metadata)
        ));
    }

    /// <summary>
    /// Estimates the number of tokens for a piece of text, using the supplied
    /// <see cref="TokenCounter"/> when available, otherwise a ~4-characters-per-token heuristic.
    /// </summary>
    private int CountTokens(string text)
    {
        if (TokenCounter != null)
            return Math.Max(0, TokenCounter(text));

        if (string.IsNullOrEmpty(text))
            return 0;

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    #endregion
}
