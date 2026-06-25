using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.Chunking;

/// <summary>
/// Chunks Markdown text by splitting at heading lines while keeping each heading attached to
/// the section that follows it. Fenced code blocks are treated as atomic and are never split,
/// and heading-looking lines inside a fence are ignored.
/// </summary>
public class MarkdownChunkingStrategy : IChunkingStrategy
{
    #region Private-Members

    private readonly ChunkingOptions _options;
    private readonly ILogger<MarkdownChunkingStrategy>? _logger;

    private static readonly Regex HeadingRegex = new Regex(@"^#{1,6}\s", RegexOptions.Compiled);
    private static readonly Regex FenceRegex = new Regex(@"^\s*(```|~~~)", RegexOptions.Compiled);

    #endregion

    /// <summary>
    /// Initializes a new instance of the MarkdownChunkingStrategy.
    /// </summary>
    /// <param name="options">The chunking options to use.</param>
    /// <param name="logger">Optional logger instance.</param>
    public MarkdownChunkingStrategy(ChunkingOptions options, ILogger<MarkdownChunkingStrategy>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    #region Public-Methods

    /// <summary>
    /// Splits a Markdown document into chunks aligned to heading boundaries.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of chunks.</returns>
    public Task<IReadOnlyList<IChunk>> ChunkAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Chunking document {DocumentId} with markdown strategy (chunk size: {ChunkSize})",
            document.DocumentId, _options.ChunkSize);

        var chunks = new List<IChunk>();
        var text = document.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.LogWarning("Document {DocumentId} has empty content, returning no chunks", document.DocumentId);
            return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
        }

        int chunkSize = _options.ChunkSize > 0 ? _options.ChunkSize : 1;

        var sections = SplitIntoSections(text);

        int chunkIndex = 0;
        foreach (var section in sections)
        {
            var sectionText = section.Text;
            if (string.IsNullOrWhiteSpace(sectionText))
                continue;

            if (sectionText.Length <= chunkSize)
            {
                AddChunk(chunks, sectionText, ref chunkIndex, document);
                continue;
            }

            // Section is too large: sub-split by paragraphs then by character window.
            foreach (var part in SplitLargeSection(sectionText, chunkSize))
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var chunkText = part;
                if (!string.IsNullOrEmpty(section.Heading) && !part.StartsWith(section.Heading, StringComparison.Ordinal))
                    chunkText = section.Heading + "\n" + part;

                AddChunk(chunks, chunkText, ref chunkIndex, document);
            }
        }

        _logger?.LogInformation("Document {DocumentId} split into {ChunkCount} chunks using markdown strategy",
            document.DocumentId, chunks.Count);

        return Task.FromResult<IReadOnlyList<IChunk>>(chunks);
    }

    #endregion

    #region Private-Methods

    private void AddChunk(List<IChunk> chunks, string text, ref int chunkIndex, IDocument document)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        chunks.Add(Chunk.Create(
            text.Trim(),
            chunkIndex++,
            document.DocumentId,
            new Dictionary<string, object>(document.Metadata)
        ));
    }

    /// <summary>
    /// Splits the markdown into sections that begin at heading lines, keeping each heading with
    /// the content that follows. Fenced code blocks are kept intact and their contents ignored
    /// for heading detection.
    /// </summary>
    private static List<Section> SplitIntoSections(string text)
    {
        var sections = new List<Section>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var current = new StringBuilder();
        string currentHeading = string.Empty;
        bool inFence = false;
        bool hasContent = false;

        foreach (var line in lines)
        {
            bool isFenceMarker = FenceRegex.IsMatch(line);

            if (isFenceMarker)
            {
                inFence = !inFence;
                current.Append(line).Append('\n');
                hasContent = true;
                continue;
            }

            if (!inFence && HeadingRegex.IsMatch(line))
            {
                // Start a new section at this heading.
                if (hasContent)
                {
                    sections.Add(new Section(currentHeading, current.ToString().Trim()));
                    current.Clear();
                }

                currentHeading = line.Trim();
                current.Append(line).Append('\n');
                hasContent = true;
            }
            else
            {
                current.Append(line).Append('\n');
                if (!string.IsNullOrWhiteSpace(line))
                    hasContent = true;
            }
        }

        if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString()))
            sections.Add(new Section(currentHeading, current.ToString().Trim()));

        return sections;
    }

    /// <summary>
    /// Sub-splits an oversized section first by paragraphs, then by character window so that no
    /// resulting piece exceeds the chunk size.
    /// </summary>
    private static List<string> SplitLargeSection(string sectionText, int chunkSize)
    {
        var result = new List<string>();
        var paragraphs = sectionText.Split(new[] { "\n\n" }, StringSplitOptions.None);

        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            var para = paragraph;
            if (string.IsNullOrWhiteSpace(para))
                continue;

            if (para.Length > chunkSize)
            {
                // Flush whatever has accumulated before handling the oversized paragraph.
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                for (int i = 0; i < para.Length; i += chunkSize)
                    result.Add(para.Substring(i, Math.Min(chunkSize, para.Length - i)));

                continue;
            }

            int separatorLength = current.Length > 0 ? 2 : 0;
            if (current.Length + separatorLength + para.Length > chunkSize && current.Length > 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
                current.Append("\n\n");
            current.Append(para);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private readonly struct Section
    {
        public Section(string heading, string text)
        {
            Heading = heading;
            Text = text;
        }

        public string Heading { get; }
        public string Text { get; }
    }

    #endregion
}
