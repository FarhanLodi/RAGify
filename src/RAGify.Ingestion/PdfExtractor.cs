using RAGify.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts text from PDF files using PdfPig library.
/// </summary>
public class PdfExtractor : IDocumentExtractor
{
    #region Private-Members

    private static readonly string[] SupportedExtensions = { ".pdf" };

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
    /// Extracts text content from a PDF file.
    /// </summary>
    /// <param name="filePath">The path to the PDF file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var textBuilder = new System.Text.StringBuilder();

            try
            {
                using var document = PdfDocument.Open(filePath);
                
                foreach (var page in document.GetPages())
                {
                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        textBuilder.AppendLine(pageText);
                        textBuilder.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from PDF: {filePath}. Error: {ex.Message}", ex);
            }

            return textBuilder.ToString().Trim();
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts text content from a PDF stream.
    /// </summary>
    /// <param name="stream">The stream containing the PDF data.</param>
    /// <param name="mimeType">Optional MIME type (not used for PDF extraction).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content.</returns>
    public async Task<string> ExtractAsync(Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var textBuilder = new System.Text.StringBuilder();

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

                using var document = PdfDocument.Open(seekableStream);
                
                foreach (var page in document.GetPages())
                {
                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        textBuilder.AppendLine(pageText);
                        textBuilder.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from PDF stream. Error: {ex.Message}", ex);
            }

            return textBuilder.ToString().Trim();
        }, cancellationToken);
    }

    #endregion
}
