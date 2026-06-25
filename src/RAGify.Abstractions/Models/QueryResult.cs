namespace RAGify.Abstractions;

/// <summary>
/// Represents the result of a query operation in the RAG system.
/// </summary>
public class QueryResult
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the list of retrieval results containing relevant chunks.
    /// </summary>
    public IReadOnlyList<RetrievalResult> Context { get; set; } = Array.Empty<RetrievalResult>();

    /// <summary>
    /// Gets or sets the metadata about the retrieval process.
    /// </summary>
    public RetrievalMetadata? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the generated natural-language answer. Populated by <c>AnswerAsync</c>;
    /// <c>null</c> for retrieval-only <c>QueryAsync</c> calls.
    /// </summary>
    public string? Answer { get; set; }

    /// <summary>
    /// Gets or sets metadata about the answer-generation step, when an answer was generated.
    /// </summary>
    public GenerationMetadata? Generation { get; set; }

    #endregion
}

/// <summary>
/// Contains metadata about the answer-generation step.
/// </summary>
public class GenerationMetadata
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the model that produced the answer.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the number of prompt (input) tokens consumed, if reported.
    /// </summary>
    public int? PromptTokens { get; set; }

    /// <summary>
    /// Gets or sets the number of completion (output) tokens produced, if reported.
    /// </summary>
    public int? CompletionTokens { get; set; }

    /// <summary>
    /// Gets or sets the reason generation stopped, if reported.
    /// </summary>
    public string? FinishReason { get; set; }

    #endregion
}

/// <summary>
/// Contains metadata about the retrieval process.
/// </summary>
public class RetrievalMetadata
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the effective Top-K value used for retrieval.
    /// </summary>
    public int EffectiveTopK { get; set; }

    /// <summary>
    /// Gets or sets the similarity threshold used for filtering results.
    /// </summary>
    public double SimilarityThreshold { get; set; }

    /// <summary>
    /// Gets or sets the detected question type (e.g., "Fact", "Explanatory", "List").
    /// </summary>
    public string? QuestionType { get; set; }

    /// <summary>
    /// Gets or sets the number of chunks before deduplication was applied.
    /// </summary>
    public int ChunksBeforeDeduplication { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether dynamic Top-K was used.
    /// </summary>
    public bool DynamicTopKUsed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether deduplication was applied.
    /// </summary>
    public bool DeduplicationApplied { get; set; }

    #endregion
}
