using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Generation.Models;

namespace RAGify.Generation;

/// <summary>
/// Chat completion provider backed by the Azure OpenAI Chat Completions API.
/// The deployment name encodes the model, so no model field is sent in the request body.
/// </summary>
public class AzureOpenAIChatProvider : ILlmProvider, IDisposable
{
    #region Private-Members

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _deploymentName;
    private readonly string _apiVersion;
    private readonly bool _disposeHttpClient;
    private readonly ILogger<AzureOpenAIChatProvider>? _logger;

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNameCaseInsensitive = true };

    #endregion

    #region Public-Members

    /// <summary>
    /// Gets the deployment name used by this provider (Azure encodes the model in the deployment).
    /// </summary>
    public string Model => _deploymentName;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIChatProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The Azure OpenAI API key.</param>
    /// <param name="deploymentName">The chat deployment name.</param>
    /// <param name="resourceName">The Azure OpenAI resource name (the subdomain of *.openai.azure.com).</param>
    /// <param name="apiVersion">The API version (default: "2024-10-21").</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created and owned by this instance.</param>
    /// <param name="logger">Optional logger.</param>
    public AzureOpenAIChatProvider(
        string apiKey,
        string deploymentName,
        string resourceName,
        string apiVersion = "2024-10-21",
        HttpClient? httpClient = null,
        ILogger<AzureOpenAIChatProvider>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _deploymentName = deploymentName ?? throw new ArgumentNullException(nameof(deploymentName));

        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Resource name cannot be null or empty.", nameof(resourceName));

        _apiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient == null;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(
            $"https://{resourceName}.openai.azure.com/openai/deployments/{deploymentName}/");

        if (!_httpClient.DefaultRequestHeaders.Contains("api-key"))
            _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

        _logger?.LogInformation("Initialized Azure OpenAI chat provider with deployment {Deployment}", _deploymentName);
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Generates a single, complete chat completion for the given messages.
    /// </summary>
    /// <param name="messages">The ordered conversation messages.</param>
    /// <param name="options">Optional generation options.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task whose result contains the chat completion.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="messages"/> is null or empty.</exception>
    public async Task<ChatCompletion> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        var request = BuildRequest(messages, options, stream: false);

        _logger?.LogDebug("Sending chat completion request to Azure OpenAI with {MessageCount} messages", messages.Count);

        var response = await _httpClient.PostAsJsonAsync(
            $"chat/completions?api-version={_apiVersion}", request, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(SerializerOptions, cancellationToken);

        if (result?.Choices == null || result.Choices.Count == 0)
        {
            _logger?.LogError("No choices returned from Azure OpenAI chat completions API");
            throw new InvalidOperationException("No choices returned from Azure OpenAI chat completions API.");
        }

        var choice = result.Choices[0];

        return new ChatCompletion
        {
            Content = choice.Message?.Content ?? string.Empty,
            Model = result.Model ?? _deploymentName,
            PromptTokens = result.Usage?.PromptTokens,
            CompletionTokens = result.Usage?.CompletionTokens,
            FinishReason = choice.FinishReason
        };
    }

    /// <summary>
    /// Streams a chat completion token-by-token for the given messages using Server-Sent Events.
    /// </summary>
    /// <param name="messages">The ordered conversation messages.</param>
    /// <param name="options">Optional generation options.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An asynchronous stream of incremental text fragments.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="messages"/> is null or empty.</exception>
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        var request = BuildRequest(messages, options, stream: true);

        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post, $"chat/completions?api-version={_apiVersion}")
        {
            Content = JsonContent.Create(request, options: SerializerOptions)
        };

        using var response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var payload = line["data: ".Length..];

            if (payload == "[DONE]")
                break;

            var content = ExtractDeltaContent(payload);

            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }

    /// <summary>
    /// Disposes the HttpClient if it was created by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposeHttpClient)
            _httpClient?.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion

    #region Private-Methods

    /// <summary>
    /// Builds the request body for the Azure OpenAI chat completions endpoint (no model field).
    /// </summary>
    private object BuildRequest(IReadOnlyList<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var payload = new Dictionary<string, object?>
        {
            ["messages"] = messages.Select(m => new { role = MapRole(m.Role), content = m.Content }).ToArray()
        };

        if (stream)
            payload["stream"] = true;

        if (options != null)
        {
            if (options.Temperature.HasValue)
                payload["temperature"] = options.Temperature.Value;

            if (options.MaxTokens.HasValue)
                payload["max_tokens"] = options.MaxTokens.Value;

            if (options.TopP.HasValue)
                payload["top_p"] = options.TopP.Value;
        }

        return payload;
    }

    /// <summary>
    /// Extracts the incremental delta content from a single SSE payload, if present.
    /// </summary>
    private string? ExtractDeltaContent(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            var first = choices[0];

            if (first.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind == JsonValueKind.String)
            {
                return contentElement.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse streaming SSE payload");
        }

        return null;
    }

    /// <summary>
    /// Maps a <see cref="ChatRole"/> to its Azure OpenAI API string representation.
    /// </summary>
    private static string MapRole(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => "user"
    };

    #endregion
}
