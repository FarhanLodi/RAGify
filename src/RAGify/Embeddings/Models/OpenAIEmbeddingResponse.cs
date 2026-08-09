namespace RAGify.Embeddings.Models;

internal class OpenAIEmbeddingResponse
{
    #region Public-Members

    public List<OpenAIEmbeddingData> Data { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
    public OpenAIUsage Usage { get; set; } = new();

    #endregion
}
