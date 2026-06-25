namespace RAGify.Abstractions;

/// <summary>
/// Options that control how a grounded answer is generated from retrieved context.
/// </summary>
public class GenerationOptions
{
    #region Public-Members

    /// <summary>
    /// Gets or sets an optional system prompt that overrides the default RAG system prompt.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Gets or sets the sampling temperature for generation.
    /// Note: some newer models reject this parameter; it may be ignored when unsupported.
    /// </summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets the maximum number of tokens to generate.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the prompt should instruct the model to cite
    /// retrieved sources (e.g., using [1], [2] markers).
    /// </summary>
    public bool IncludeCitations { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional custom prompt template. When provided, the tokens
    /// <c>{context}</c> and <c>{query}</c> are replaced with the retrieved context and the user query.
    /// </summary>
    public string? PromptTemplate { get; set; }

    #endregion
}
