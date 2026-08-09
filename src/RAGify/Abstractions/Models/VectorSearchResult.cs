namespace RAGify.Abstractions;

/// <summary>
/// Represents a result from a vector search operation.
/// </summary>
public class VectorSearchResult
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the unique identifier of the vector.
    /// </summary>
    public string VectorId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the similarity score between the query vector and this vector (0.0 to 1.0).
    /// </summary>
    public double Similarity { get; set; }

    /// <summary>
    /// Gets or sets the metadata associated with the vector.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    #endregion
}
