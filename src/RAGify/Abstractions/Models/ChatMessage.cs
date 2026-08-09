namespace RAGify.Abstractions;

/// <summary>
/// Represents a single message in a chat conversation.
/// </summary>
public class ChatMessage
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the role of the message author.
    /// </summary>
    public ChatRole Role { get; set; } = ChatRole.User;

    /// <summary>
    /// Gets or sets the text content of the message.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMessage"/> class.
    /// </summary>
    public ChatMessage()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMessage"/> class with the specified role and content.
    /// </summary>
    /// <param name="role">The author role.</param>
    /// <param name="content">The message text.</param>
    public ChatMessage(ChatRole role, string content)
    {
        Role = role;
        Content = content;
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Creates a system message.
    /// </summary>
    /// <param name="content">The message text.</param>
    /// <returns>A new system <see cref="ChatMessage"/>.</returns>
    public static ChatMessage System(string content) => new(ChatRole.System, content);

    /// <summary>
    /// Creates a user message.
    /// </summary>
    /// <param name="content">The message text.</param>
    /// <returns>A new user <see cref="ChatMessage"/>.</returns>
    public static ChatMessage User(string content) => new(ChatRole.User, content);

    /// <summary>
    /// Creates an assistant message.
    /// </summary>
    /// <param name="content">The message text.</param>
    /// <returns>A new assistant <see cref="ChatMessage"/>.</returns>
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);

    #endregion
}
