using System.Text.RegularExpressions;

namespace RAGify.Core;

/// <summary>
/// Provides functionality to clean and normalize text before processing.
/// </summary>
public class TextCleanupService
{
    #region Private-Members

    private static readonly Regex TimestampRegex = new Regex(
        @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b|\b\d{4}[/-]\d{1,2}[/-]\d{1,2}\b|\b\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AP]M)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UrlRegex = new Regex(
        @"https?://[^\s]+|www\.[^\s]+|ftp://[^\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NavigationMenuRegex = new Regex(
        @"\b(Home|Contact|About|Shop|Products|Services|Login|Sign\s*Up|Menu|Navigation|Skip\s*to\s*content)\s*[|•·]\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex RepeatedWhitespaceRegex = new Regex(
        @"\s{3,}",
        RegexOptions.Compiled);

    private static readonly Regex RepeatedNewlinesRegex = new Regex(
        @"\n{3,}",
        RegexOptions.Compiled);

    private static readonly Regex RepeatedPunctuationRegex = new Regex(
        @"[.!?]{3,}",
        RegexOptions.Compiled);

    #endregion

    #region Public-Methods

    /// <summary>
    /// Cleans and normalizes text based on the provided options.
    /// </summary>
    /// <param name="text">The text to clean.</param>
    /// <param name="options">Optional cleanup options. If not provided, default options will be used.</param>
    /// <returns>The cleaned text.</returns>
    public static string CleanText(string text, TextCleanupOptions? options = null)
    {
        options ??= new TextCleanupOptions();

        if (string.IsNullOrWhiteSpace(text))
            return text;

        var cleaned = text;

        if (options.RemoveTimestamps)
        {
            cleaned = TimestampRegex.Replace(cleaned, string.Empty);
        }

        if (options.RemoveUrls)
        {
            cleaned = UrlRegex.Replace(cleaned, string.Empty);
        }

        if (options.RemoveNavigationText)
        {
            cleaned = NavigationMenuRegex.Replace(cleaned, string.Empty);
        }

        if (options.CollapseWhitespace)
        {
            cleaned = RepeatedWhitespaceRegex.Replace(cleaned, " ");
        }

        if (options.CollapseNewlines)
        {
            cleaned = RepeatedNewlinesRegex.Replace(cleaned, "\n\n");
        }

        if (options.RemoveRepeatedPunctuation)
        {
            cleaned = RepeatedPunctuationRegex.Replace(cleaned, ".");
        }

        cleaned = cleaned.Trim();
        cleaned = cleaned.Replace("\r\n", "\n").Replace("\r", "\n");

        return cleaned;
    }

    #endregion
}

/// <summary>
/// Options for configuring text cleanup behavior.
/// </summary>
public class TextCleanupOptions
{
    #region Public-Members

    /// <summary>
    /// Gets or sets a value indicating whether to remove timestamps from text. Default is true.
    /// </summary>
    public bool RemoveTimestamps { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to remove URLs from text. Default is true.
    /// </summary>
    public bool RemoveUrls { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to remove navigation menu text. Default is true.
    /// </summary>
    public bool RemoveNavigationText { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to collapse multiple whitespace characters into single spaces. Default is true.
    /// </summary>
    public bool CollapseWhitespace { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to collapse multiple newlines into single newlines. Default is true.
    /// </summary>
    public bool CollapseNewlines { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to remove repeated punctuation marks. Default is true.
    /// </summary>
    public bool RemoveRepeatedPunctuation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether text cleanup is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    #endregion
}
