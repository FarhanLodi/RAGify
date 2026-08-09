namespace RAGify.Core;

/// <summary>
/// Options for configuring document chunking behavior.
/// </summary>
public class ChunkingOptions
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the target size of each chunk in characters. Default is 600.
    /// </summary>
    public int ChunkSize { get; set; } = 600;

    /// <summary>
    /// Gets or sets the overlap size between consecutive chunks in characters. Default is 100.
    /// </summary>
    public int OverlapSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets a value indicating whether to respect sentence boundaries when chunking. Default is true.
    /// </summary>
    public bool RespectSentenceBoundaries { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to respect token boundaries when chunking. Default is false.
    /// </summary>
    public bool RespectTokenBoundaries { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum number of sentences per chunk. Default is 5.
    /// </summary>
    public int? MaxSentencesPerChunk { get; set; } = 5;

    #endregion
}
