using System.Text;
using RAGify.Abstractions;

namespace RAGify.Generation;

/// <summary>
/// Builds the chat message sequence used to ground an LLM answer in retrieved context.
/// Produces either a system + user message pair (default) or a single templated user message
/// when a custom <see cref="GenerationOptions.PromptTemplate"/> is supplied.
/// </summary>
public class RagPromptBuilder
{
    #region Private-Members

    private const string DefaultSystemPromptWithCitations =
        "You are a helpful assistant that answers questions using ONLY the provided context. " +
        "Do not use any prior knowledge. If the context does not contain enough information to " +
        "answer the question, say that you don't know based on the provided context. " +
        "When you use information from the context, cite the relevant sources inline using their " +
        "bracketed numbers (e.g., [1], [2]).";

    private const string DefaultSystemPromptNoCitations =
        "You are a helpful assistant that answers questions using ONLY the provided context. " +
        "Do not use any prior knowledge. If the context does not contain enough information to " +
        "answer the question, say that you don't know based on the provided context.";

    private const string EmptyContextSystemPrompt =
        "You are a helpful assistant. No context was retrieved for the user's question. " +
        "Answer that you cannot find any relevant information to answer the question.";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="RagPromptBuilder"/> class.
    /// </summary>
    public RagPromptBuilder()
    {
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Builds the chat messages for a grounded answer from the query and retrieved context.
    /// </summary>
    /// <param name="query">The user's question.</param>
    /// <param name="context">The retrieved context chunks to ground the answer in.</param>
    /// <param name="options">Optional generation options. If not provided, sensible defaults are used.</param>
    /// <returns>
    /// A read-only list of chat messages. When <see cref="GenerationOptions.PromptTemplate"/> is set,
    /// a single user message is returned; otherwise a system message followed by a user message.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    public IReadOnlyList<ChatMessage> Build(
        string query,
        IReadOnlyList<RetrievalResult> context,
        GenerationOptions? options = null)
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        options ??= new GenerationOptions();
        context ??= Array.Empty<RetrievalResult>();

        var numberedContext = BuildNumberedContext(context);

        // Custom template path: single user message with token substitution.
        if (!string.IsNullOrEmpty(options.PromptTemplate))
        {
            var rendered = options.PromptTemplate
                .Replace("{context}", numberedContext)
                .Replace("{query}", query);

            return new[] { ChatMessage.User(rendered) };
        }

        // Default path: system + user message pair.
        var systemPrompt = ResolveSystemPrompt(options, context.Count == 0);
        var userMessage = $"Context:\n{numberedContext}\n\nQuestion: {query}";

        return new[]
        {
            ChatMessage.System(systemPrompt),
            ChatMessage.User(userMessage)
        };
    }

    #endregion

    #region Private-Methods

    /// <summary>
    /// Renders the retrieved chunks as a numbered, source-attributed context block.
    /// Each entry is rendered as "[n] (Source: {source})\n{text}" and entries are joined by a blank line.
    /// </summary>
    private static string BuildNumberedContext(IReadOnlyList<RetrievalResult> context)
    {
        if (context.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();

        for (var i = 0; i < context.Count; i++)
        {
            var result = context[i];
            var source = result.Source ?? result.Chunk.DocumentId;

            if (i > 0)
                builder.Append("\n\n");

            builder.Append('[').Append(i + 1).Append("] (Source: ").Append(source).Append(")\n");
            builder.Append(result.Chunk.Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Resolves the system prompt based on the supplied options and whether any context is available.
    /// </summary>
    private static string ResolveSystemPrompt(GenerationOptions options, bool isContextEmpty)
    {
        if (!string.IsNullOrEmpty(options.SystemPrompt))
            return options.SystemPrompt;

        if (isContextEmpty)
            return EmptyContextSystemPrompt;

        return options.IncludeCitations
            ? DefaultSystemPromptWithCitations
            : DefaultSystemPromptNoCitations;
    }

    #endregion
}
