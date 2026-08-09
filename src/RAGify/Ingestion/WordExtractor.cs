using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RAGify.Abstractions;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts text from Word documents (.docx) with paragraph-based extraction.
/// </summary>
public class WordExtractor : IDocumentExtractor
{
    #region Private-Members

    private static readonly string[] SupportedExtensions = { ".docx" };

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
    /// Extracts text content from a Word document file.
    /// </summary>
    /// <param name="filePath">The path to the Word document file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var wordDocument = WordprocessingDocument.Open(filePath, false);
                var body = wordDocument.MainDocumentPart?.Document?.Body;

                if (body == null)
                    return string.Empty;

                var paragraphs = new List<string>();

                foreach (var paragraph in body.Elements<Paragraph>())
                {
                    var text = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        paragraphs.Add(text.Trim());
                    }
                }

                return string.Join("\n\n", paragraphs);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from Word document: {filePath}. Error: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts text content from a Word document stream.
    /// </summary>
    /// <param name="stream">The stream containing the Word document data.</param>
    /// <param name="mimeType">Optional MIME type (not used for Word extraction).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                Stream seekableStream = stream;
                if (!stream.CanSeek)
                {
                    var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Position = 0;
                    seekableStream = memoryStream;
                }

                using var wordDocument = WordprocessingDocument.Open(seekableStream, false);
                var body = wordDocument.MainDocumentPart?.Document?.Body;

                if (body == null)
                    return string.Empty;

                var paragraphs = new List<string>();

                foreach (var paragraph in body.Elements<Paragraph>())
                {
                    var text = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        paragraphs.Add(text.Trim());
                    }
                }

                return string.Join("\n\n", paragraphs);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from Word document stream. Error: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    #endregion
}
