using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Generation;

namespace RAGify;

/// <summary>
/// Fluent configuration extensions for the generation ("G" in RAG) stage of the pipeline.
/// </summary>
public partial class RagifyConfig
{
    #region Public-Methods

    /// <summary>
    /// Configures a custom LLM (chat completion) provider used to generate grounded answers.
    /// </summary>
    /// <param name="provider">The LLM provider to use.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithLlm(ILlmProvider provider)
    {
        _llmProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    /// <summary>
    /// Configures the generation options used when producing grounded answers.
    /// </summary>
    /// <param name="options">The generation options to use.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithGenerationOptions(GenerationOptions options)
    {
        _generationOptions = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    /// Configures an OpenAI chat completion provider for generation.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Chat model name (default: "gpt-4o-mini").</param>
    /// <param name="baseUrl">Base URL for the API (default: https://api.openai.com/v1/).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithOpenAIChat(
        string apiKey,
        string model = "gpt-4o-mini",
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _llmProvider = new OpenAIChatProvider(apiKey, model, baseUrl, httpClient);
        return this;
    }

    /// <summary>
    /// Configures an Azure OpenAI chat completion provider for generation.
    /// </summary>
    /// <param name="apiKey">Azure OpenAI API key.</param>
    /// <param name="deploymentName">The chat deployment name.</param>
    /// <param name="resourceName">Azure OpenAI resource name.</param>
    /// <param name="apiVersion">API version (default: "2024-10-21").</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithAzureOpenAIChat(
        string apiKey,
        string deploymentName,
        string resourceName,
        string apiVersion = "2024-10-21",
        HttpClient? httpClient = null)
    {
        _llmProvider = new AzureOpenAIChatProvider(apiKey, deploymentName, resourceName, apiVersion, httpClient);
        return this;
    }

    /// <summary>
    /// Configures an Anthropic (Claude) chat completion provider for generation.
    /// </summary>
    /// <param name="apiKey">Anthropic API key.</param>
    /// <param name="model">Chat model name (default: "claude-opus-4-8").</param>
    /// <param name="baseUrl">Base URL for the API (default: provider default).</param>
    /// <param name="maxTokens">Maximum number of tokens to generate (default: 1024).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithAnthropicChat(
        string apiKey,
        string model = "claude-opus-4-8",
        string? baseUrl = null,
        int maxTokens = 1024,
        HttpClient? httpClient = null)
    {
        _llmProvider = new AnthropicChatProvider(apiKey, model, baseUrl, maxTokens, httpClient);
        return this;
    }

    /// <summary>
    /// Configures an Ollama chat completion provider for local or remote Ollama instances.
    /// </summary>
    /// <param name="model">Chat model name (default: "llama3.2").</param>
    /// <param name="baseUrl">Base URL for the Ollama API (default: provider default).</param>
    /// <param name="httpClient">Optional HttpClient. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithOllamaChat(
        string model = "llama3.2",
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        _llmProvider = new OllamaChatProvider(model, baseUrl, httpClient);
        return this;
    }

    #endregion
}
