using RAGify.Abstractions;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts text from plain text files.
/// </summary>
public class PlainTextExtractor : IDocumentExtractor
{
    #region Private-Members

    private static readonly string[] SupportedExtensions = { ".txt", ".md", ".csv", ".log" };

    #endregion

    #region Public-Methods

    /// <summary>
    /// Determines whether this extractor can handle the specified file path.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if this extractor can handle the file; otherwise, false.</returns>
    public bool CanExtract(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// Extracts text content from a plain text file.
    /// </summary>
    /// <param name="filePath">The path to the text file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Extracts text content from a plain text stream.
    /// </summary>
    /// <param name="stream">The stream containing the text data.</param>
    /// <param name="mimeType">Optional MIME type (not used for plain text extraction).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    #endregion
}
