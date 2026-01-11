using RAGify.Abstractions;
using RAGify;
using RAGify.Core;
using RAGify.Ingestion;
using Microsoft.Extensions.Logging;

namespace RAGify.ConsoleTest;

class Program
{
    private static IRagify? _ragify;
    private static DocumentIngestionService? _ingestionService;
    private static ILoggerFactory? _loggerFactory;
    private static QueryOptions _queryOptions = new()
    {
        Retrieval = new RetrievalOptions 
        { 
            TopK = 0, // Use dynamic Top-K
            SimilarityThreshold = 0.35, // Default refined threshold
            EnableDynamicTopK = true,
            EnableDeduplication = true
        }
    };

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== RAGify Console Test Application ===\n");
        Console.WriteLine("Initializing RAG system...\n");

        // Configure logging
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddConsole(options =>
                {
                    options.FormatterName = "simple";
                })
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                    options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                })
                .SetMinimumLevel(LogLevel.Information); // Change to LogLevel.Debug for detailed logs
        });

        var logger = _loggerFactory.CreateLogger<Ragify>();

        // Initialize RAG system with Ollama (default)
        // Note: Make sure Ollama is running and the model is available
        // Run: ollama pull all-minilm:latest
        _ragify = new RagifyConfig()
            .WithChunking(ChunkingStrategyType.SentenceAware, new ChunkingOptions
            {
                ChunkSize = 600, // Improved smaller chunk size
                OverlapSize = 100, // Reduced overlap
                RespectSentenceBoundaries = true,
                MaxSentencesPerChunk = 5 // Better granularity
            })
            .WithTextCleanup(new TextCleanupOptions
            {
                Enabled = true,
                RemoveTimestamps = true,
                RemoveUrls = true,
                RemoveNavigationText = true,
                CollapseWhitespace = true,
                CollapseNewlines = true,
                RemoveRepeatedPunctuation = true
            })
            .WithOllamaEmbeddings("all-minilm:latest")
            .WithInMemoryVectorStore()
            .WithDefaultExtractors()
            .WithLogger(logger)  // Enable logging
            .Build();

        // Automatically use default extractors (PDF, PlainText, etc.)
        // The system will automatically detect file types based on extension
        _ingestionService = DocumentIngestionService.CreateDefault();

        Console.WriteLine("RAG system initialized successfully!\n");
        Console.WriteLine("Logging is enabled. You'll see detailed operation logs below.\n");

        // Main menu loop
        bool running = true;
        while (running)
        {
            ShowMenu();
            var choice = Console.ReadLine()?.Trim();

            try
            {
                switch (choice)
                {
                    case "1":
                        await IngestDocument();
                        break;
                    case "2":
                        await IngestRawText();
                        break;
                    case "3":
                        await ViewIndexedDocuments();
                        break;
                    case "4":
                        await ViewChunks();
                        break;
                    case "5":
                        await AskQuestion();
                        break;
                    case "6":
                        await AdjustSettings();
                        break;
                    case "7":
                        await ClearVectorStore();
                        break;
                    case "8":
                        running = false;
                        break;
                    default:
                        Console.WriteLine("Invalid choice. Please try again.\n");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}\n");
            }
        }

        Console.WriteLine("Goodbye!");
    }

    static void ShowMenu()
    {
        Console.WriteLine("=== Main Menu ===");
        Console.WriteLine("1. Ingest document from file");
        Console.WriteLine("2. Ingest raw text");
        Console.WriteLine("3. View indexed documents");
        Console.WriteLine("4. View chunks for a document");
        Console.WriteLine("5. Ask a question (RAG query)");
        Console.WriteLine("6. Adjust runtime settings");
        Console.WriteLine("7. Clear vector store");
        Console.WriteLine("8. Exit");
        Console.Write("\nEnter your choice: ");
    }

    static async Task IngestDocument()
    {
        Console.Write("\nEnter file path: ");
        var filePath = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Console.WriteLine("File not found or path is empty.\n");
            return;
        }

        Console.WriteLine("Ingesting document...");
        var document = await _ingestionService!.IngestFromFileAsync(filePath);
        await _ragify!.IngestAsync(document);

        Console.WriteLine($"Document ingested successfully! Document ID: {document.DocumentId}\n");
    }

    static async Task IngestRawText()
    {
        Console.Write("\nEnter source identifier: ");
        var source = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(source))
        {
            source = "raw_text_input";
        }

        Console.WriteLine("Enter text (press Enter twice to finish):");
        var lines = new List<string>();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line) && lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                break;
            lines.Add(line);
        }

        var text = string.Join("\n", lines).Trim();
        if (string.IsNullOrEmpty(text))
        {
            Console.WriteLine("No text provided.\n");
            return;
        }

        var document = _ingestionService!.IngestFromText(text, source);
        await _ragify!.IngestAsync(document);

        Console.WriteLine($"Text ingested successfully! Document ID: {document.DocumentId}\n");
    }

    static async Task ViewIndexedDocuments()
    {
        var documents = await _ragify!.GetIndexedDocumentsAsync();

        Console.WriteLine($"\n=== Indexed Documents ({documents.Count}) ===");
        if (documents.Count == 0)
        {
            Console.WriteLine("No documents indexed.\n");
            return;
        }

        foreach (var docId in documents)
        {
            Console.WriteLine($"  - {docId}");
        }
        Console.WriteLine();
    }

    static async Task ViewChunks()
    {
        Console.Write("\nEnter document ID: ");
        var docId = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(docId))
        {
            Console.WriteLine("Document ID is required.\n");
            return;
        }

        var chunks = await _ragify!.GetChunksAsync(docId);

        Console.WriteLine($"\n=== Chunks for Document: {docId} ({chunks.Count} chunks) ===");
        if (chunks.Count == 0)
        {
            Console.WriteLine("No chunks found for this document.\n");
            return;
        }

        foreach (var chunk in chunks)
        {
            Console.WriteLine($"\n[Chunk {chunk.Index}]");
            Console.WriteLine($"ID: {chunk.ChunkId}");
            Console.WriteLine($"Length: {chunk.Text.Length} characters");
            var preview = chunk.Text.Length > 300 
                ? chunk.Text.Substring(0, 300) + "..." 
                : chunk.Text;
            Console.WriteLine($"Text (cleaned): {preview}");
        }
        Console.WriteLine();
    }

    static async Task AskQuestion()
    {
        Console.Write("\nEnter your question: ");
        var query = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(query))
        {
            Console.WriteLine("Query cannot be empty.\n");
            return;
        }

        Console.WriteLine("\n=== Query Settings ===");
        Console.WriteLine($"Top-K: {(_queryOptions.Retrieval.TopK > 0 ? _queryOptions.Retrieval.TopK.ToString() : "Dynamic")}");
        Console.WriteLine($"Similarity Threshold: {_queryOptions.Retrieval.SimilarityThreshold:F2}");
        Console.WriteLine($"Dynamic Top-K: {(_queryOptions.Retrieval.EnableDynamicTopK ? "Enabled" : "Disabled")}");
        Console.WriteLine($"Deduplication: {(_queryOptions.Retrieval.EnableDeduplication ? "Enabled" : "Disabled")}");

        Console.WriteLine("\nProcessing query...");
        var result = await _ragify!.QueryAsync(query, _queryOptions);

        // Display retrieval metadata if available
        if (result.Metadata != null)
        {
            Console.WriteLine("\n=== Retrieval Metadata ===");
            Console.WriteLine($"Effective Top-K Used: {result.Metadata.EffectiveTopK}");
            Console.WriteLine($"Question Type Detected: {result.Metadata.QuestionType ?? "Unknown"}");
            Console.WriteLine($"Chunks Before Deduplication: {result.Metadata.ChunksBeforeDeduplication}");
            Console.WriteLine($"Dynamic Top-K Used: {(result.Metadata.DynamicTopKUsed ? "Yes" : "No")}");
            Console.WriteLine($"Deduplication Applied: {(result.Metadata.DeduplicationApplied ? "Yes" : "No")}");
        }

        Console.WriteLine("\n=== Retrieved Context ===");
        if (result.Context.Count == 0)
        {
            Console.WriteLine("No relevant context found.\n");
            Console.WriteLine("Tip: Try lowering the similarity threshold or check if documents are indexed.\n");
            return;
        }

        Console.WriteLine($"Retrieved {result.Context.Count} chunk(s):\n");

        for (int i = 0; i < result.Context.Count; i++)
        {
            var ctx = result.Context[i];
            Console.WriteLine($"\n[Context {i + 1}]");
            Console.WriteLine($"Similarity Score: {ctx.Similarity:F4}");
            Console.WriteLine($"Document: {ctx.Chunk.DocumentId}");
            if (!string.IsNullOrEmpty(ctx.Source))
            {
                Console.WriteLine($"Source: {ctx.Source}");
            }
            if (ctx.Page.HasValue)
            {
                Console.WriteLine($"Page: {ctx.Page}");
            }
            Console.WriteLine($"Chunk Index: {ctx.Chunk.Index}");
            Console.WriteLine($"Text Length: {ctx.Chunk.Text.Length} characters");
            Console.WriteLine($"Text (cleaned): {ctx.Chunk.Text}");
            Console.WriteLine($"---");
        }

        Console.WriteLine("\nOptions:");
        Console.WriteLine("  [R] Re-run with same question");
        Console.WriteLine("  [A] Adjust settings and re-run");
        Console.WriteLine("  [Enter] Return to menu");
        Console.Write("\nChoice: ");
        var choice = Console.ReadLine()?.Trim().ToLower();

        if (choice == "r")
        {
            await AskQuestion(); // Re-run same question
        }
        else if (choice == "a")
        {
            await AdjustSettings();
            await AskQuestion(); // Re-run with new settings
        }
        Console.WriteLine();
    }

    static async Task AdjustSettings()
    {
        Console.WriteLine("\n=== Current Settings ===");
        Console.WriteLine($"Top-K: {(_queryOptions.Retrieval.TopK > 0 ? _queryOptions.Retrieval.TopK.ToString() : "Dynamic")}");
        Console.WriteLine($"Similarity Threshold: {_queryOptions.Retrieval.SimilarityThreshold:F2}");
        Console.WriteLine($"Dynamic Top-K: {(_queryOptions.Retrieval.EnableDynamicTopK ? "Enabled" : "Disabled")}");
        Console.WriteLine($"Deduplication: {(_queryOptions.Retrieval.EnableDeduplication ? "Enabled" : "Disabled")}");

        Console.WriteLine("\nAdjust settings:");
        Console.Write("Top-K (0 for dynamic, current: {0}): ", _queryOptions.Retrieval.TopK > 0 ? _queryOptions.Retrieval.TopK.ToString() : "Dynamic");
        var topKInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(topKInput) && int.TryParse(topKInput, out int topK) && topK >= 0)
        {
            _queryOptions.Retrieval.TopK = topK;
            if (topK == 0)
            {
                _queryOptions.Retrieval.EnableDynamicTopK = true;
            }
        }

        Console.Write("Similarity Threshold (0.0-1.0, current: {0:F2}): ", _queryOptions.Retrieval.SimilarityThreshold);
        var thresholdInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(thresholdInput) && double.TryParse(thresholdInput, out double threshold) && threshold >= 0 && threshold <= 1)
        {
            _queryOptions.Retrieval.SimilarityThreshold = threshold;
        }

        Console.Write("Enable Dynamic Top-K? (y/n, current: {0}): ", _queryOptions.Retrieval.EnableDynamicTopK ? "y" : "n");
        var dynamicInput = Console.ReadLine()?.Trim().ToLower();
        if (!string.IsNullOrEmpty(dynamicInput))
        {
            _queryOptions.Retrieval.EnableDynamicTopK = dynamicInput == "y";
        }

        Console.Write("Enable Deduplication? (y/n, current: {0}): ", _queryOptions.Retrieval.EnableDeduplication ? "y" : "n");
        var dedupInput = Console.ReadLine()?.Trim().ToLower();
        if (!string.IsNullOrEmpty(dedupInput))
        {
            _queryOptions.Retrieval.EnableDeduplication = dedupInput == "y";
        }

        Console.WriteLine("\nSettings updated!\n");
    }

    static async Task ClearVectorStore()
    {
        Console.Write("\nAre you sure you want to clear all indexed data? (y/n): ");
        var confirm = Console.ReadLine()?.Trim().ToLower();

        if (confirm == "y")
        {
            await _ragify!.ClearAsync();
            Console.WriteLine("Vector store cleared successfully!\n");
        }
        else
        {
            Console.WriteLine("Operation cancelled.\n");
        }
    }
}
