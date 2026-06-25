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
    SlidingWindow,

    /// <summary>
    /// Recursive chunking that splits on a hierarchy of separators
    /// (paragraphs, then lines, then sentences, then words) to keep chunks under the size limit.
    /// </summary>
    Recursive,

    /// <summary>
    /// Markdown-aware chunking that splits on heading boundaries and keeps code fences intact.
    /// </summary>
    Markdown,

    /// <summary>
    /// Token-aware chunking that sizes chunks by an estimated token count rather than characters.
    /// </summary>
    TokenAware
}
