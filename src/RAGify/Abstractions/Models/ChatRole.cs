namespace RAGify.Abstractions;

/// <summary>
/// Identifies the author role of a chat message.
/// </summary>
public enum ChatRole
{
    /// <summary>
    /// A system message that establishes the assistant's behavior and context.
    /// </summary>
    System,

    /// <summary>
    /// A message authored by the end user.
    /// </summary>
    User,

    /// <summary>
    /// A message authored by the assistant (the model).
    /// </summary>
    Assistant
}
