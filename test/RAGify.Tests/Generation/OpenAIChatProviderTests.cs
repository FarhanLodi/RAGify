using System.Net;
using System.Text;
using RAGify.Abstractions;
using RAGify.Generation;

namespace RAGify.Tests;

/// <summary>
/// Unit tests for <see cref="OpenAIChatProvider"/> using a stubbed HTTP handler (offline, deterministic).
/// </summary>
public class OpenAIChatProviderTests
{
    #region Private-Types

    /// <summary>
    /// Stub <see cref="HttpMessageHandler"/> that captures the request body and returns a canned response.
    /// </summary>
    private class StubHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public string? CapturedRequestBody { get; private set; }
        public Uri? CapturedRequestUri { get; private set; }

        public StubHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedRequestUri = request.RequestUri;

            if (request.Content != null)
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    #endregion

    #region Private-Members

    private const string CannedResponse = """
    {
      "id": "chatcmpl-123",
      "object": "chat.completion",
      "model": "gpt-4o-mini",
      "choices": [
        {
          "index": 0,
          "message": { "role": "assistant", "content": "The answer is 42." },
          "finish_reason": "stop"
        }
      ],
      "usage": {
        "prompt_tokens": 17,
        "completion_tokens": 5,
        "total_tokens": 22
      }
    }
    """;

    #endregion

    #region Public-Methods

    [Fact]
    public async Task CompleteAsync_ReturnsExpectedContentAndTokenCounts()
    {
        var handler = new StubHandler(CannedResponse);
        using var client = new HttpClient(handler);
        using var provider = new OpenAIChatProvider("k", httpClient: client);

        var messages = new[]
        {
            ChatMessage.System("You are helpful."),
            ChatMessage.User("What is the answer?")
        };

        var completion = await provider.CompleteAsync(messages);

        Assert.Equal("The answer is 42.", completion.Content);
        Assert.Equal(17, completion.PromptTokens);
        Assert.Equal(5, completion.CompletionTokens);
        Assert.Equal("stop", completion.FinishReason);
        Assert.Equal("gpt-4o-mini", completion.Model);
    }

    [Fact]
    public async Task CompleteAsync_SendsModelAndMessagesInRequestBody()
    {
        var handler = new StubHandler(CannedResponse);
        using var client = new HttpClient(handler);
        using var provider = new OpenAIChatProvider("k", model: "gpt-4o-mini", httpClient: client);

        var messages = new[]
        {
            ChatMessage.System("You are helpful."),
            ChatMessage.User("Hello there")
        };

        await provider.CompleteAsync(messages);

        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("\"model\"", handler.CapturedRequestBody);
        Assert.Contains("gpt-4o-mini", handler.CapturedRequestBody);
        Assert.Contains("\"messages\"", handler.CapturedRequestBody);
        Assert.Contains("Hello there", handler.CapturedRequestBody);
        Assert.Contains("system", handler.CapturedRequestBody);
        Assert.Contains("user", handler.CapturedRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_IncludesOptionsWhenProvided()
    {
        var handler = new StubHandler(CannedResponse);
        using var client = new HttpClient(handler);
        using var provider = new OpenAIChatProvider("k", httpClient: client);

        var messages = new[] { ChatMessage.User("Hi") };
        var options = new ChatOptions { Temperature = 0.5, MaxTokens = 128, TopP = 0.9 };

        await provider.CompleteAsync(messages, options);

        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("temperature", handler.CapturedRequestBody);
        Assert.Contains("max_tokens", handler.CapturedRequestBody);
        Assert.Contains("top_p", handler.CapturedRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_OmitsOptionsWhenNotProvided()
    {
        var handler = new StubHandler(CannedResponse);
        using var client = new HttpClient(handler);
        using var provider = new OpenAIChatProvider("k", httpClient: client);

        var messages = new[] { ChatMessage.User("Hi") };

        await provider.CompleteAsync(messages);

        Assert.NotNull(handler.CapturedRequestBody);
        Assert.DoesNotContain("temperature", handler.CapturedRequestBody);
        Assert.DoesNotContain("max_tokens", handler.CapturedRequestBody);
        Assert.DoesNotContain("top_p", handler.CapturedRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_ThrowsForEmptyMessages()
    {
        var handler = new StubHandler(CannedResponse);
        using var client = new HttpClient(handler);
        using var provider = new OpenAIChatProvider("k", httpClient: client);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.CompleteAsync(Array.Empty<ChatMessage>()));
    }

    #endregion
}
