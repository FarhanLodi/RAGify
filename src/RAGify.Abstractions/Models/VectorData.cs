namespace RAGify.Abstractions;

/// <summary>
/// Represents vector data with metadata for storage and retrieval.
/// </summary>
public class VectorData
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the unique identifier for the vector.
    /// </summary>
    public string VectorId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the vector embedding array.
    /// </summary>
    public float[] Vector { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Gets or sets the metadata associated with the vector.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    #endregion
}
