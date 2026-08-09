namespace RAGify.Embeddings.Models;

internal class OpenAIEmbeddingData
{
    #region Public-Members

    public int Index { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public string Object { get; set; } = string.Empty;

    #endregion
}
