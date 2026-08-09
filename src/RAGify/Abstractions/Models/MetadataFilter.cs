namespace RAGify.Abstractions;

/// <summary>
/// Represents a filter for vector search based on metadata.
/// </summary>
public class MetadataFilter
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the dictionary of metadata filters. Keys are metadata field names, values are the expected values.
    /// </summary>
    public Dictionary<string, object> Filters { get; set; } = new();

    #endregion
}
