using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RAGify.Abstractions;
using Microsoft.Extensions.Logging;

namespace RAGify.Generation;

/// <summary>
/// LLM provider backed by a local or remote Ollama instance using the <c>/api/chat</c> endpoint.
/// Supports single-shot completion and newline-delimited JSON streaming.
/// </summary>
public class OllamaChatProvider : ILlmProvider, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly ILogger<OllamaChatProvider>? _logger;
    private readonly string _model;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
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
    /// Initializes a new instance of the <see cref="OllamaChatProvider"/> class.
    /// </summary>
    /// <param name="model">The Ollama model name (e.g., "llama3.2").</param>
    /// <param name="baseUrl">Base URL for the Ollama API (default: http://localhost:11434/).</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, a new one will be created and disposed by this instance.</param>
    /// <param name="logger">Optional logger.</param>
    public OllamaChatProvider(
        string model = "llama3.2",
        string? baseUrl = null,
        HttpClient? httpClient = null,
        ILogger<OllamaChatProvider>? logger = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _logger = logger;

        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;

        var url = baseUrl ?? "http://localhost:11434/";
        if (!url.EndsWith("/"))
            url += "/";

        _httpClient.BaseAddress = new Uri(url);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _logger?.LogDebug("Sending Ollama chat request for model {Model}.", _model);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to Ollama. Ensure Ollama is running and the model is pulled: " +
                $"ollama pull {_model}. Error: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new InvalidOperationException(
                $"Request to Ollama timed out. Ensure Ollama is running and the model is pulled: " +
                $"ollama pull {_model}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Ollama API error: {errorContent}. Ensure Ollama is running and the model is pulled: " +
                    $"ollama pull {_model}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            OllamaChatResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<OllamaChatResponse>(responseJson, _serializerOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse response from Ollama. Ensure Ollama is running and the model is pulled: " +
                    $"ollama pull {_model}. Error: {ex.Message}", ex);
            }

            if (parsed == null)
                throw new InvalidOperationException("Failed to parse response from the Ollama /api/chat endpoint.");

            return new ChatCompletion
            {
                Content = parsed.Message?.Content ?? string.Empty,
                Model = parsed.Model,
                PromptTokens = parsed.PromptEvalCount,
                CompletionTokens = parsed.EvalCount
            };
        }
    }

    /// <summary>
    /// Streams a chat completion as incremental text fragments using Ollama's newline-delimited JSON stream.
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _logger?.LogDebug("Sending Ollama streaming chat request for model {Model}.", _model);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to Ollama. Ensure Ollama is running and the model is pulled: " +
                $"ollama pull {_model}. Error: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new InvalidOperationException(
                $"Request to Ollama timed out. Ensure Ollama is running and the model is pulled: " +
                $"ollama pull {_model}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Ollama API error: {errorContent}. Ensure Ollama is running and the model is pulled: " +
                    $"ollama pull {_model}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                line = line.Trim();
                if (line.Length == 0)
                    continue;

                var (text, done) = ParseStreamLine(line);
                if (text != null)
                    yield return text;
                if (done)
                    break;
            }
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
    /// Builds the Ollama <c>/api/chat</c> request body from the conversation and options.
    /// </summary>
    private Dictionary<string, object?> BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        bool stream)
    {
        var apiMessages = new List<object>();
        foreach (var message in messages)
        {
            apiMessages.Add(new
            {
                role = MapRole(message.Role),
                content = message.Content
            });
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["messages"] = apiMessages,
            ["stream"] = stream
        };

        var optionsObject = new Dictionary<string, object?>();
        if (options?.Temperature.HasValue == true)
            optionsObject["temperature"] = options.Temperature.Value;
        if (options?.TopP.HasValue == true)
            optionsObject["top_p"] = options.TopP.Value;
        if (options?.MaxTokens.HasValue == true)
            optionsObject["num_predict"] = options.MaxTokens.Value;

        if (optionsObject.Count > 0)
            body["options"] = optionsObject;

        return body;
    }

    /// <summary>
    /// Maps a <see cref="ChatRole"/> to the corresponding Ollama role string.
    /// </summary>
    private static string MapRole(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.Assistant => "assistant",
        _ => "user"
    };

    /// <summary>
    /// Parses a single newline-delimited JSON object from the Ollama stream.
    /// Returns the incremental content (if present) and whether the stream is done.
    /// </summary>
    private static (string? Text, bool Done) ParseStreamLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            string? text = null;
            if (root.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.Object
                && messageElement.TryGetProperty("content", out var contentElement))
            {
                text = contentElement.GetString();
            }

            var done = root.TryGetProperty("done", out var doneElement)
                && doneElement.ValueKind == JsonValueKind.True;

            return (text, done);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }

    #endregion

    #region Response-DTOs

    /// <summary>
    /// Non-streaming response shape from the Ollama <c>/api/chat</c> endpoint.
    /// </summary>
    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("message")]
        public OllamaChatMessage? Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }
    }

    /// <summary>
    /// A chat message within an Ollama response.
    /// </summary>
    private sealed class OllamaChatMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    #endregion
}
