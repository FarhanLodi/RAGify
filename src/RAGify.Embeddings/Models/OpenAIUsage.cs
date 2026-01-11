namespace RAGify.Embeddings.Models;

internal class OpenAIUsage
{
    #region Public-Members

    public int PromptTokens { get; set; }
    public int TotalTokens { get; set; }

    #endregion
}
