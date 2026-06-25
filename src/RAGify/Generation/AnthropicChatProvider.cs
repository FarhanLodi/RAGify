using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RAGify.Abstractions;
using Microsoft.Extensions.Logging;

namespace RAGify.Generation;

/// <summary>
/// LLM provider backed by the Anthropic Messages API (Claude models).
/// Implements both single-shot completion and Server-Sent Events (SSE) streaming.
/// </summary>
public class AnthropicChatProvider : ILlmProvider, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly ILogger<AnthropicChatProvider>? _logger;
    private readonly string _model;
    private readonly int _maxTokens;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Model id fragments that cause the Anthropic Messages API to reject the
    /// <c>temperature</c> and <c>top_p</c> sampling parameters with HTTP 400.
    /// </summary>
    private static readonly string[] _samplingRejectModelFragments =
    {
        "opus-4-8",
        "opus-4-7",
        "fable",
        "mythos"
    };

    #endregion

    #region Public-Members

    /// <summary>
    /// Gets the model identifier used by this provider.
    /// </summary>
    public string Model => _model;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AnthropicChatProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The Anthropic API key. Sent as the <c>x-api-key</c> header.</param>
    /// <param name="model">The Claude model id (e.g., "claude-opus-4-8").</param>
    /// <param name="baseUrl">Base URL for the Anthropic API (default: https://api.anthropic.com/v1/).</param>
    /// <param name="maxTokens">Default maximum tokens to generate when <see cref="ChatOptions.MaxTokens"/> is not provided.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, a new one will be created and disposed by this instance.</param>
    /// <param name="logger">Optional logger.</param>
    public AnthropicChatProvider(
        string apiKey,
        string model = "claude-opus-4-8",
        string? baseUrl = null,
        int maxTokens = 1024,
        HttpClient? httpClient = null,
        ILogger<AnthropicChatProvider>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));

        _model = model ?? throw new ArgumentNullException(nameof(model));
        _maxTokens = maxTokens;
        _logger = logger;

        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;

        var url = baseUrl ?? "https://api.anthropic.com/v1/";
        if (!url.EndsWith("/"))
            url += "/";

        _httpClient.BaseAddress = new Uri(url);

        if (!_httpClient.DefaultRequestHeaders.Contains("x-api-key"))
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        if (!_httpClient.DefaultRequestHeaders.Contains("anthropic-version"))
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Generates a single, complete chat completion for the given messages.
    /// </summary>
    /// <param name="messages">The ordered conversation messages (system, user, assistant).</param>
    /// <param name="options">Optional generation options. If not provided, provider defaults are used.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the chat completion.</returns>
    public async Task<ChatCompletion> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        var body = BuildRequestBody(messages, options, stream: false);
        var json = JsonSerializer.Serialize(body, _serializerOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _logger?.LogDebug("Sending Anthropic completion request for model {Model}.", _model);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<AnthropicMessageResponse>(responseJson, _serializerOptions);

        if (parsed == null)
            throw new InvalidOperationException("Failed to parse response from the Anthropic Messages API.");

        var contentBuilder = new StringBuilder();
        if (parsed.Content != null)
        {
            foreach (var block in parsed.Content)
            {
                if (block != null && block.Type == "text" && block.Text != null)
                    contentBuilder.Append(block.Text);
            }
        }

        return new ChatCompletion
        {
            Content = contentBuilder.ToString(),
            Model = parsed.Model,
            PromptTokens = parsed.Usage?.InputTokens,
            CompletionTokens = parsed.Usage?.OutputTokens,
            FinishReason = parsed.StopReason
        };
    }

    /// <summary>
    /// Streams a chat completion as incremental text fragments using the Anthropic Messages API SSE stream.
    /// </summary>
    /// <param name="messages">The ordered conversation messages (system, user, assistant).</param>
    /// <param name="options">Optional generation options. If not provided, provider defaults are used.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An asynchronous stream of incremental text fragments that together form the answer.</returns>
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        var body = BuildRequestBody(messages, options, stream: true);
        var json = JsonSerializer.Serialize(body, _serializerOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _logger?.LogDebug("Sending Anthropic streaming request for model {Model}.", _model);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var payload = line.Substring("data:".Length).Trim();
            if (payload.Length == 0)
                continue;

            var text = ExtractTextDelta(payload);
            if (text != null)
                yield return text;
        }
    }

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/> if it was created by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposeHttpClient)
            _httpClient?.Dispose();
    }

    #endregion

    #region Private-Methods

    /// <summary>
    /// Builds the Anthropic Messages API request body from the conversation and options.
    /// </summary>
    private Dictionary<string, object?> BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        bool stream)
    {
        var systemBuilder = new StringBuilder();
        var apiMessages = new List<object>();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (systemBuilder.Length > 0)
                    systemBuilder.Append("\n\n");
                systemBuilder.Append(message.Content);
            }
            else
            {
                apiMessages.Add(new
                {
                    role = message.Role == ChatRole.Assistant ? "assistant" : "user",
                    content = message.Content
                });
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["max_tokens"] = options?.MaxTokens ?? _maxTokens,
            ["messages"] = apiMessages
        };

        if (systemBuilder.Length > 0)
            body["system"] = systemBuilder.ToString();

        if (!RejectsSamplingParameters(_model))
        {
            if (options?.Temperature.HasValue == true)
                body["temperature"] = options.Temperature.Value;
            if (options?.TopP.HasValue == true)
                body["top_p"] = options.TopP.Value;
        }

        if (options?.StopSequences != null && options.StopSequences.Count > 0)
            body["stop_sequences"] = options.StopSequences;

        if (stream)
            body["stream"] = true;

        return body;
    }

    /// <summary>
    /// Determines whether the given model rejects the <c>temperature</c>/<c>top_p</c> sampling parameters.
    /// </summary>
    private static bool RejectsSamplingParameters(string model)
    {
        foreach (var fragment in _samplingRejectModelFragments)
        {
            if (model.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parses an SSE <c>data:</c> payload and returns the incremental text when it is a
    /// <c>content_block_delta</c> carrying a <c>text_delta</c>; otherwise returns null.
    /// </summary>
    private static string? ExtractTextDelta(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeElement)
                || typeElement.GetString() != "content_block_delta")
                return null;

            if (!root.TryGetProperty("delta", out var deltaElement))
                return null;

            if (!deltaElement.TryGetProperty("type", out var deltaTypeElement)
                || deltaTypeElement.GetString() != "text_delta")
                return null;

            if (deltaElement.TryGetProperty("text", out var textElement))
                return textElement.GetString();

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    #endregion

    #region Response-DTOs

    /// <summary>
    /// Non-streaming response shape from the Anthropic Messages API.
    /// </summary>
    private sealed class AnthropicMessageResponse
    {
        [JsonPropertyName("content")]
        public List<AnthropicContentBlock>? Content { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }

        [JsonPropertyName("usage")]
        public AnthropicUsage? Usage { get; set; }
    }

    /// <summary>
    /// A single content block within an Anthropic message response.
    /// </summary>
    private sealed class AnthropicContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    /// <summary>
    /// Token usage reported by the Anthropic Messages API.
    /// </summary>
    private sealed class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int? InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int? OutputTokens { get; set; }
    }

    #endregion
}
