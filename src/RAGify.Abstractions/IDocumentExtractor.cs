namespace RAGify.Abstractions;

/// <summary>
/// Provides functionality to extract text content from various document formats.
/// </summary>
public interface IDocumentExtractor
{
    #region Public-Methods

    /// <summary>
    /// Determines whether this extractor can handle the specified file path.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if this extractor can handle the file; otherwise, false.</returns>
    bool CanExtract(string filePath);

    /// <summary>
    /// Extracts text content from a file.
    /// </summary>
    /// <param name="filePath">The path to the file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts text content from a stream.
    /// </summary>
    /// <param name="stream">The stream to extract from.</param>
    /// <param name="mimeType">Optional MIME type to help determine the extraction method.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default);

    #endregion
}
