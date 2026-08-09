# Changelog

All notable changes to **RAGify** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.0.0] - 2026-08-09

> One package to rule them all: the eight RAGify packages are now a **single `RAGify` package and a single assembly**.
> This is a **packaging‑only** change — namespaces and the public API are untouched, so your code compiles as‑is.

### Changed
- **RAGify now ships as exactly one NuGet package: `RAGify`.** The seven secondary packages have been folded into it and are discontinued.
- The former sub‑projects now live inside `src/RAGify/` as subfolders (`Abstractions/`, `Core/`, `Chunking/`, `Embeddings/`, `Ingestion/`, `Retrieval/`, `VectorStores/`); their individual `.csproj` files are gone.
- All third‑party dependencies that previously sat on the sub‑projects are now direct dependencies of the single `RAGify` package: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.ML.OnnxRuntime`, `DocumentFormat.OpenXml`, `HtmlAgilityPack`, `UglyToad.PdfPig`, `Npgsql`, `Pinecone.Net`, `WeaviateNET`.

### Removed
- NuGet package `RAGify.Abstractions` — discontinued, now part of `RAGify`.
- NuGet package `RAGify.Core` — discontinued, now part of `RAGify`.
- NuGet package `RAGify.Chunking` — discontinued, now part of `RAGify`.
- NuGet package `RAGify.Embeddings` — discontinued, now part of `RAGify`.
- NuGet package `RAGify.Ingestion` — discontinued, now part of `RAGify`.
- NuGet package `RAGify.Retrieval` — discontinued, now part of `RAGify`.
- NuGet package `RAGify.VectorStores` — discontinued, now part of `RAGify`.

### Breaking Changes
- **The break is purely at the packaging level.** Projects referencing any `RAGify.*` package will no longer restore, because those package IDs are no longer published.
- **What is _not_ breaking:** C# namespaces are unchanged. `RAGify.Abstractions`, `RAGify.Core`, `RAGify.Chunking`, `RAGify.Embeddings`, `RAGify.Ingestion`, `RAGify.Retrieval`, `RAGify.VectorStores`, `RAGify.Generation`, and `RAGify.Reranking` all still exist as namespaces inside the single `RAGify` assembly. There are **no `using` directive changes and no API, type, or signature changes** — existing code compiles as‑is.

### Migration
- Replace every `<PackageReference Include="RAGify.*" />` in your project files with a single reference:

  ```xml
  <PackageReference Include="RAGify" Version="3.0.0" />
  ```

- Leave your source code alone — no `using` statements need to change.

### Notes
- Consumers who previously installed only a narrow sub‑package (e.g. just `RAGify.Chunking`) will now pull the **full dependency set** listed under _Changed_ above, since those dependencies belong to the unified package.

## [2.0.0] - 2026-06-25

> The release that completes the loop: RAGify now does both **retrieval _and_ generation**.
> Generation, reranking, embedding caching, and dependency‑injection support all ship **inside the main `RAGify` package** — no new packages to install.

### Added

#### 🤖 Answer Generation (the "G" in RAG)
- New `ILlmProvider` abstraction with four implementations: **OpenAI** (`OpenAIChatProvider`), **Azure OpenAI** (`AzureOpenAIChatProvider`), **Anthropic / Claude** (`AnthropicChatProvider`), and **Ollama** (`OllamaChatProvider`).
- `IRagify.AnswerAsync(...)` — retrieves context and returns a grounded, cited natural‑language answer.
- `IRagify.StreamAnswerAsync(...)` — token‑by‑token streaming via `IAsyncEnumerable<string>`.
- `RagPromptBuilder` for context‑grounded prompt assembly with inline citations (`[1]`, `[2]`, …).
- New models: `ChatMessage`, `ChatRole`, `ChatOptions`, `ChatCompletion`, `GenerationOptions`; `QueryResult` now carries `Answer` and `Generation` (model + token usage) metadata.
- Builder methods: `WithLlm`, `WithOpenAIChat`, `WithAzureOpenAIChat`, `WithAnthropicChat`, `WithOllamaChat`, `WithGenerationOptions`.

#### 🥇 Reranking
- New `IReranker` abstraction with `CohereReranker` (Cohere Rerank API) and a dependency‑free `LexicalReranker` (BM25).
- Reranking is wired into the retrieval pipeline as a second stage after vector search.
- Builder methods: `WithReranker`, `WithCohereReranker`, `WithLexicalReranker`.

#### ⚡ Embedding Cache & Resilience
- New `IEmbeddingCache` abstraction with `InMemoryEmbeddingCache`.
- `CachingEmbeddingProvider` decorator to avoid re‑embedding identical inputs.
- `BatchingEmbeddingProvider` to respect provider per‑request batch limits.
- `RetryDelegatingHandler` + `ResilientHttpClientFactory` — automatic retry/backoff on 429/5xx honoring `Retry-After`.
- Builder methods: `WithEmbeddingCache`, `WithInMemoryEmbeddingCache`.

#### ✂️ New Chunking Strategies
- `ChunkingStrategyType.Recursive` — hierarchical splitter (paragraphs → lines → sentences → words).
- `ChunkingStrategyType.Markdown` — heading‑aware splitting that keeps fenced code blocks intact.
- `ChunkingStrategyType.TokenAware` — sizes chunks by estimated tokens (with a pluggable `TokenCounter`), activating the previously unused token‑boundary concept.

#### 🗂️ New Document Extractors
- `MarkdownExtractor` (`.md`, `.markdown`), `CsvExtractor` (`.csv`, `.tsv`), `JsonExtractor` (`.json`, `.jsonl`).
- `WebPageExtractor` + `DocumentIngestionService.IngestFromUrlAsync(...)` for ingesting web pages by URL.
- New extractors are included in `WithDefaultExtractors()` and `DocumentIngestionService.CreateDefault()`.

#### 🧩 Dependency Injection
- `AddRagify(this IServiceCollection, ...)` extensions for `Microsoft.Extensions.DependencyInjection`.

#### 💾 Fluent Vector‑Store Helpers
- `WithQdrantVectorStore`, `WithPgVectorStore`, `WithPineconeVectorStore`, `WithWeaviateVectorStore` (previously these stores required manual construction via `WithVectorStore`).

#### ✅ Tests
- Added a comprehensive unit + integration test suite (`RAGify.Tests`) covering generation, reranking, caching, new chunkers/extractors, the end‑to‑end pipeline, and regression tests for the bug fixes below.

### Changed
- `IRagify` now includes `AnswerAsync` and `StreamAnswerAsync`.
- `Ragify` and `RetrievalEngine` constructors gained **optional** parameters for the LLM/generation and reranker (existing call sites are unaffected).
- `RagifyConfig` is now a `partial class`, split across focused files for each capability.
- README fully restructured for clarity and onboarding.

### Fixed
- **FixedSizeChunkingStrategy** no longer emits a duplicate trailing overlap chunk at end‑of‑text, and now guards against non‑termination when `OverlapSize >= ChunkSize`.
- **SentenceAwareChunkingStrategy** now preserves sentence‑ending punctuation (`.`/`!`/`?`) and splits oversized single sentences instead of emitting one giant chunk.
- **Ragify.ClearAsync** now clears the retrieval engine's chunk cache — no more stale results returned after a reset.
- **QdrantVectorStore.ClearAsync** semaphore misuse fixed (balanced `Wait`/`Release`; no longer swallows all exceptions).
- **PgVectorStore** now formats vector/number literals with `InvariantCulture`, so it works correctly under comma‑decimal locales (e.g. `de-DE`).
- **WeaviateVectorStore** search now uses the real Weaviate **GraphQL** API (`/v1/graphql`) instead of a non‑existent REST endpoint, so vector search works against a live server.

### Breaking Changes
- `IRagify` gained `AnswerAsync` / `StreamAnswerAsync` — **custom `IRagify` implementations must add these members**.
- Chunk output for `FixedSize` and `SentenceAware` may differ from 1.x due to the correctness fixes above — re‑ingest if you persisted chunk IDs or positions.

### Notes
- Generation, reranking, and DI live in the main **`RAGify`** package — there are **no new NuGet packages** to add.

## [1.0.0] - 2026-01-11

### Added
- Initial release: document ingestion (PDF, Word, Excel, HTML, plain text), chunking (Fixed Size, Sentence‑Aware, Sliding Window), 8 embedding providers, 5 vector stores, a retrieval engine, the `RagifyConfig` fluent builder, and `Microsoft.Extensions.Logging` integration.

[3.0.0]: https://github.com/FarhanLodi/RAGify/releases/tag/v3.0.0
[2.0.0]: https://github.com/FarhanLodi/RAGify/releases/tag/v2.0.0
[1.0.0]: https://github.com/FarhanLodi/RAGify/releases/tag/v1.0.0
