using System.Net;
using System.Text;
using System.Text.Json;
using RAGify.Abstractions;
using RAGify.Generation;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="AnthropicChatProvider"/> using a stubbed <see cref="HttpMessageHandler"/>.
/// </summary>
public class AnthropicChatProviderTests
{
    #region Stub-Handler

    /// <summary>
    /// Captures the outbound request body and returns a canned Anthropic Messages API response.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public string? CapturedBody { get; private set; }

        public StubHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content != null)
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    #endregion

    #region Helpers

    private const string CannedResponse =
        "{" +
        "\"content\":[{\"type\":\"text\",\"text\":\"Hello \"},{\"type\":\"text\",\"text\":\"world\"}]," +
        "\"stop_reason\":\"end_turn\"," +
        "\"model\":\"claude-opus-4-8\"," +
        "\"usage\":{\"input_tokens\":12,\"output_tokens\":7}" +
        "}";

    #endregion

    #region Tests

    [Fact]
    public async Task CompleteAsync_ReturnsConcatenatedTextAndTokenCounts()
    {
        var handler = new StubHandler(CannedResponse);
        var httpClient = new HttpClient(handler);
        using var provider = new AnthropicChatProvider(
            apiKey: "test-key",
            model: "claude-opus-4-8",
            httpClient: httpClient);

        var messages = new List<ChatMessage>
        {
            ChatMessage.User("Say hello.")
        };

        var result = await provider.CompleteAsync(messages);

        Assert.Equal("Hello world", result.Content);
        Assert.Equal("claude-opus-4-8", result.Model);
        Assert.Equal(12, result.PromptTokens);
        Assert.Equal(7, result.CompletionTokens);
        Assert.Equal("end_turn", result.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_PutsSystemMessageAtTopLevel_AndExcludesItFromMessages()
    {
        var handler = new StubHandler(CannedResponse);
        var httpClient = new HttpClient(handler);
        using var provider = new AnthropicChatProvider(
            apiKey: "test-key",
            model: "claude-opus-4-8",
            httpClient: httpClient);

        var messages = new List<ChatMessage>
        {
            ChatMessage.System("You are a helpful assistant."),
            ChatMessage.User("Hi.")
        };

        await provider.CompleteAsync(messages);

        Assert.NotNull(handler.CapturedBody);
        using var document = JsonDocument.Parse(handler.CapturedBody!);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("system", out var systemElement));
        Assert.Equal("You are a helpful assistant.", systemElement.GetString());

        Assert.True(root.TryGetProperty("messages", out var messagesElement));
        Assert.Equal(JsonValueKind.Array, messagesElement.ValueKind);
        foreach (var entry in messagesElement.EnumerateArray())
        {
            Assert.True(entry.TryGetProperty("role", out var roleElement));
            Assert.NotEqual("system", roleElement.GetString());
        }
    }

    [Fact]
    public async Task CompleteAsync_OmitsTemperature_ForSamplingRejectingModel()
    {
        var handler = new StubHandler(CannedResponse);
        var httpClient = new HttpClient(handler);
        using var provider = new AnthropicChatProvider(
            apiKey: "test-key",
            model: "claude-opus-4-8",
            httpClient: httpClient);

        var messages = new List<ChatMessage>
        {
            ChatMessage.User("Hi.")
        };

        var options = new ChatOptions { Temperature = 0.7, TopP = 0.9 };

        await provider.CompleteAsync(messages, options);

        Assert.NotNull(handler.CapturedBody);
        using var document = JsonDocument.Parse(handler.CapturedBody!);
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("top_p", out _));
    }

    #endregion
}
