namespace RAGify.Embeddings.Models;

internal class GoogleGeminiEmbeddingResponse
{
    #region Public-Members

    public GoogleGeminiEmbeddingData? Embedding { get; set; }

    #endregion
}

internal class GoogleGeminiBatchEmbeddingResponse
{
    #region Public-Members

    public List<GoogleGeminiEmbeddingData> Embeddings { get; set; } = new();

    #endregion
}

internal class GoogleGeminiEmbeddingData
{
    #region Public-Members

    public List<float> Values { get; set; } = new();

    #endregion
}
