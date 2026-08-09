using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using RAGify.Abstractions;
using System.Text;

namespace RAGify.Ingestion;

/// <summary>
/// Extracts text from Excel documents (.xlsx) with sheet and row metadata.
/// </summary>
public class ExcelExtractor : IDocumentExtractor
{
    #region Private-Members

    private static readonly string[] SupportedExtensions = { ".xlsx" };

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
    /// Extracts text content from an Excel file.
    /// </summary>
    /// <param name="filePath">The path to the Excel file to extract from.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content with sheet information.</returns>
    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var spreadsheetDocument = SpreadsheetDocument.Open(filePath, false);
                var workbookPart = spreadsheetDocument.WorkbookPart;

                if (workbookPart == null)
                    return string.Empty;

                var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
                var result = new StringBuilder();

                foreach (var worksheetPart in workbookPart.WorksheetParts)
                {
                    var worksheet = worksheetPart.Worksheet;
                    if (worksheet == null)
                        continue;
                    
                    var sheetData = worksheet.GetFirstChild<SheetData>();

                    if (sheetData == null)
                        continue;

                    var sheetName = GetSheetName(workbookPart, worksheetPart);
                    result.AppendLine($"=== Sheet: {sheetName} ===");

                    foreach (var row in sheetData.Elements<Row>())
                    {
                        var rowText = new List<string>();

                        foreach (var cell in row.Elements<Cell>())
                        {
                            var cellValue = GetCellValue(cell, sharedStringTable);
                            if (!string.IsNullOrWhiteSpace(cellValue))
                            {
                                rowText.Add(cellValue);
                            }
                        }

                        if (rowText.Count > 0)
                        {
                            result.AppendLine(string.Join(" | ", rowText));
                        }
                    }

                    result.AppendLine();
                }

                return result.ToString().Trim();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from Excel document: {filePath}. Error: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts text content from an Excel stream.
    /// </summary>
    /// <param name="stream">The stream containing the Excel data.</param>
    /// <param name="mimeType">Optional MIME type (not used for Excel extraction).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the extracted text content with sheet information.</returns>
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

                using var spreadsheetDocument = SpreadsheetDocument.Open(seekableStream, false);
                var workbookPart = spreadsheetDocument.WorkbookPart;

                if (workbookPart == null)
                    return string.Empty;

                var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
                var result = new StringBuilder();

                foreach (var worksheetPart in workbookPart.WorksheetParts)
                {
                    var worksheet = worksheetPart.Worksheet;
                    if (worksheet == null) continue;
                    
                    var sheetData = worksheet.GetFirstChild<SheetData>();

                    if (sheetData == null)
                        continue;

                    var sheetName = GetSheetName(workbookPart, worksheetPart);
                    result.AppendLine($"=== Sheet: {sheetName} ===");

                    foreach (var row in sheetData.Elements<Row>())
                    {
                        var rowText = new List<string>();

                        foreach (var cell in row.Elements<Cell>())
                        {
                            var cellValue = GetCellValue(cell, sharedStringTable);
                            if (!string.IsNullOrWhiteSpace(cellValue))
                            {
                                rowText.Add(cellValue);
                            }
                        }

                        if (rowText.Count > 0)
                        {
                            result.AppendLine(string.Join(" | ", rowText));
                        }
                    }

                    result.AppendLine();
                }

                return result.ToString().Trim();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from Excel document stream. Error: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    #endregion

    #region Private-Methods

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStringTable)
    {
        if (cell.CellValue == null)
            return string.Empty;

        var value = cell.CellValue.Text;

        if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString && sharedStringTable != null)
        {
            if (int.TryParse(value, out int index) && index >= 0 && index < sharedStringTable.Elements<SharedStringItem>().Count())
            {
                var sharedStringItem = sharedStringTable.Elements<SharedStringItem>().ElementAt(index);
                return sharedStringItem.Text?.Text ?? string.Empty;
            }
        }

        return value;
    }

    private static string GetSheetName(WorkbookPart workbookPart, WorksheetPart worksheetPart)
    {
        var sheet = workbookPart.Workbook?.Sheets?.Elements<Sheet>()
            .FirstOrDefault(s => s.Id?.Value == workbookPart.GetIdOfPart(worksheetPart));

        return sheet?.Name?.Value ?? "Unknown";
    }

    #endregion
}
