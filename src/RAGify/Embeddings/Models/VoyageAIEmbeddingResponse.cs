namespace RAGify.Embeddings.Models;

internal class VoyageAIEmbeddingResponse
{
    #region Public-Members

    public List<VoyageAIEmbeddingData> Data { get; set; } = new();

    #endregion
}

internal class VoyageAIEmbeddingData
{
    #region Public-Members

    public List<float> Embedding { get; set; } = new();

    #endregion
}
