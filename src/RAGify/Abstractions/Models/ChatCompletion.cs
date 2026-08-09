namespace RAGify.Abstractions;

/// <summary>
/// Represents the result of a chat completion produced by an <see cref="ILlmProvider"/>.
/// </summary>
public class ChatCompletion
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the generated text content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model that produced the completion.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the number of prompt (input) tokens consumed, if reported by the provider.
    /// </summary>
    public int? PromptTokens { get; set; }

    /// <summary>
    /// Gets or sets the number of completion (output) tokens produced, if reported by the provider.
    /// </summary>
    public int? CompletionTokens { get; set; }

    /// <summary>
    /// Gets or sets the reason generation stopped (e.g., "stop", "length"), if reported by the provider.
    /// </summary>
    public string? FinishReason { get; set; }

    #endregion
}
