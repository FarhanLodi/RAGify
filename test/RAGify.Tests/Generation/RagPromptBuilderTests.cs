using RAGify.Abstractions;
using RAGify.Generation;

namespace RAGify.Tests;

/// <summary>
/// Unit tests for <see cref="RagPromptBuilder"/>.
/// </summary>
public class RagPromptBuilderTests
{
    #region Private-Types

    /// <summary>
    /// Minimal fake chunk for constructing <see cref="RetrievalResult"/> instances in tests.
    /// </summary>
    private class FakeChunk : IChunk
    {
        public string ChunkId { get; init; } = Guid.NewGuid().ToString();
        public string Text { get; init; } = string.Empty;
        public int Index { get; init; }
        public string DocumentId { get; init; } = "doc";
        public IReadOnlyDictionary<string, object> Metadata { get; init; } =
            new Dictionary<string, object>();
    }

    #endregion

    #region Private-Methods

    private static RetrievalResult MakeResult(string text, string? source, string documentId, int index)
    {
        return new RetrievalResult
        {
            Chunk = new FakeChunk { Text = text, DocumentId = documentId, Index = index },
            Similarity = 0.9,
            Source = source
        };
    }

    #endregion

    #region Public-Methods

    [Fact]
    public void Build_ReturnsSystemAndUserMessagePair()
    {
        var builder = new RagPromptBuilder();
        var context = new[]
        {
            MakeResult("The sky is blue.", "weather.txt", "doc1", 0)
        };

        var messages = builder.Build("Why is the sky blue?", context);

        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);
    }

    [Fact]
    public void Build_NumbersContextEntries()
    {
        var builder = new RagPromptBuilder();
        var context = new[]
        {
            MakeResult("First fact.", "a.txt", "doc1", 0),
            MakeResult("Second fact.", "b.txt", "doc2", 1)
        };

        var messages = builder.Build("Question?", context);
        var userContent = messages[1].Content;

        Assert.Contains("[1] (Source: a.txt)", userContent);
        Assert.Contains("First fact.", userContent);
        Assert.Contains("[2] (Source: b.txt)", userContent);
        Assert.Contains("Second fact.", userContent);
    }

    [Fact]
    public void Build_FallsBackToDocumentIdWhenSourceIsNull()
    {
        var builder = new RagPromptBuilder();
        var context = new[]
        {
            MakeResult("Some text.", null, "document-123", 0)
        };

        var messages = builder.Build("Question?", context);

        Assert.Contains("[1] (Source: document-123)", messages[1].Content);
    }

    [Fact]
    public void Build_IncludesQueryInUserMessage()
    {
        var builder = new RagPromptBuilder();
        var context = new[]
        {
            MakeResult("Context text.", "src.txt", "doc1", 0)
        };

        var messages = builder.Build("What is RAG?", context);

        Assert.Contains("What is RAG?", messages[1].Content);
    }

    [Fact]
    public void Build_HonorsCustomPromptTemplate_ReturnsSingleMessage()
    {
        var builder = new RagPromptBuilder();
        var context = new[]
        {
            MakeResult("Grounding text.", "src.txt", "doc1", 0)
        };
        var options = new GenerationOptions
        {
            PromptTemplate = "Use this context:\n{context}\nAnswer: {query}"
        };

        var messages = builder.Build("My question", context, options);

        Assert.Single(messages);
        Assert.Equal(ChatRole.User, messages[0].Role);
        Assert.Contains("Grounding text.", messages[0].Content);
        Assert.Contains("[1] (Source: src.txt)", messages[0].Content);
        Assert.Contains("Answer: My question", messages[0].Content);
        Assert.DoesNotContain("{context}", messages[0].Content);
        Assert.DoesNotContain("{query}", messages[0].Content);
    }

    [Fact]
    public void Build_UsesSystemPromptOverride()
    {
        var builder = new RagPromptBuilder();
        var context = new[]
        {
            MakeResult("Some context.", "src.txt", "doc1", 0)
        };
        var options = new GenerationOptions
        {
            SystemPrompt = "CUSTOM SYSTEM INSTRUCTIONS"
        };

        var messages = builder.Build("Question?", context, options);

        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal("CUSTOM SYSTEM INSTRUCTIONS", messages[0].Content);
    }

    [Fact]
    public void Build_WithEmptyContext_StillReturnsSystemAndUserPair()
    {
        var builder = new RagPromptBuilder();

        var messages = builder.Build("Question with no context?", Array.Empty<RetrievalResult>());

        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Contains("Question with no context?", messages[1].Content);
    }

    #endregion
}
