namespace RAGify.Abstractions;

/// <summary>
/// Provides chat completion (text generation) capabilities for the "G" in RAG.
/// Implementations wrap an LLM provider such as OpenAI, Azure OpenAI, Anthropic, or Ollama.
/// </summary>
public interface ILlmProvider
{
    #region Public-Members

    /// <summary>
    /// Gets the model identifier used by this provider.
    /// </summary>
    string Model { get; }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Generates a single, complete chat completion for the given messages.
    /// </summary>
    /// <param name="messages">The ordered conversation messages (system, user, assistant).</param>
    /// <param name="options">Optional generation options. If not provided, provider defaults are used.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the chat completion.</returns>
    Task<ChatCompletion> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chat completion token-by-token (or chunk-by-chunk) for the given messages.
    /// </summary>
    /// <param name="messages">The ordered conversation messages (system, user, assistant).</param>
    /// <param name="options">Optional generation options. If not provided, provider defaults are used.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An asynchronous stream of incremental text fragments that together form the answer.</returns>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    #endregion
}
