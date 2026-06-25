using System.Text.Json.Serialization;

namespace RAGify.Generation.Models;

/// <summary>
/// Internal DTO for an OpenAI-compatible chat completions response.
/// </summary>
internal class ChatCompletionResponse
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the model that produced the completion.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the list of completion choices.
    /// </summary>
    public List<ChatCompletionChoice>? Choices { get; set; }

    /// <summary>
    /// Gets or sets token usage information.
    /// </summary>
    public ChatCompletionUsage? Usage { get; set; }

    #endregion
}

/// <summary>
/// Internal DTO for a single chat completion choice.
/// </summary>
internal class ChatCompletionChoice
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the assistant message for this choice.
    /// </summary>
    public ChatCompletionMessage? Message { get; set; }

    /// <summary>
    /// Gets or sets the reason generation stopped for this choice.
    /// </summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    #endregion
}

/// <summary>
/// Internal DTO for a chat completion message body.
/// </summary>
internal class ChatCompletionMessage
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the role of the message author.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets the text content of the message.
    /// </summary>
    public string? Content { get; set; }

    #endregion
}

/// <summary>
/// Internal DTO for chat completion token usage.
/// </summary>
internal class ChatCompletionUsage
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the number of prompt (input) tokens consumed.
    /// </summary>
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    /// <summary>
    /// Gets or sets the number of completion (output) tokens produced.
    /// </summary>
    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }

    /// <summary>
    /// Gets or sets the total number of tokens used.
    /// </summary>
    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }

    #endregion
}
