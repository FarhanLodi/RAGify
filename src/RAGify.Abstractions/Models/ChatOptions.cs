namespace RAGify.Abstractions;

/// <summary>
/// Options that control how an <see cref="ILlmProvider"/> generates a completion.
/// </summary>
public class ChatOptions
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the sampling temperature. Higher values produce more random output.
    /// Note: some newer models reject this parameter; providers may ignore it when unsupported.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of tokens to generate in the completion.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Gets or sets the nucleus sampling probability mass.
    /// </summary>
    public double? TopP { get; set; }

    /// <summary>
    /// Gets or sets optional stop sequences that halt generation when produced.
    /// </summary>
    public IReadOnlyList<string>? StopSequences { get; set; }

    #endregion
}
