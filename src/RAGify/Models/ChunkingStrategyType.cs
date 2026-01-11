namespace RAGify;

/// <summary>
/// Specifies the type of chunking strategy to use for document processing.
/// </summary>
public enum ChunkingStrategyType
{
    /// <summary>
    /// Fixed-size chunking with optional overlap.
    /// </summary>
    FixedSize,

    /// <summary>
    /// Sentence-aware chunking that respects sentence boundaries.
    /// </summary>
    SentenceAware,

    /// <summary>
    /// Sliding window chunking with overlap.
    /// </summary>
    SlidingWindow
}
