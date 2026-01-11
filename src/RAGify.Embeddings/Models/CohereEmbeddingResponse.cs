namespace RAGify.Embeddings.Models;

internal class CohereEmbeddingResponse
{
    #region Public-Members

    public List<List<float>> Embeddings { get; set; } = new();

    #endregion
}
