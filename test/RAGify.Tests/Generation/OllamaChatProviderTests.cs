using System.Net;
using System.Text;
using RAGify.Abstractions;
using RAGify.Generation;

namespace RAGify.Tests;

/// <summary>
/// Tests for <see cref="OllamaChatProvider"/> using a stubbed <see cref="HttpMessageHandler"/>.
/// </summary>
public class OllamaChatProviderTests
{
    #region Stub-Handler

    /// <summary>
    /// Captures the outbound request body and returns a canned Ollama /api/chat response.
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

    #region Tests

    [Fact]
    public async Task CompleteAsync_MapsContentAndTokenCounts()
    {
        const string cannedResponse =
            "{" +
            "\"model\":\"llama3.2\"," +
            "\"message\":{\"role\":\"assistant\",\"content\":\"Hello from Ollama.\"}," +
            "\"done\":true," +
            "\"prompt_eval_count\":15," +
            "\"eval_count\":9" +
            "}";

        var handler = new StubHandler(cannedResponse);
        var httpClient = new HttpClient(handler);
        using var provider = new OllamaChatProvider(
            model: "llama3.2",
            httpClient: httpClient);

        var messages = new List<ChatMessage>
        {
            ChatMessage.User("Hi.")
        };

        var result = await provider.CompleteAsync(messages);

        Assert.Equal("Hello from Ollama.", result.Content);
        Assert.Equal("llama3.2", result.Model);
        Assert.Equal(15, result.PromptTokens);
        Assert.Equal(9, result.CompletionTokens);
    }

    #endregion
}
