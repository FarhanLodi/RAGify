using RAGify.Abstractions;
using RAGify.VectorStores;

namespace RAGify;

/// <summary>
/// Fluent configuration helpers for attaching concrete vector store implementations.
/// </summary>
public partial class RagifyConfig
{
    #region Public-Methods

    #region Qdrant

    /// <summary>
    /// Configures a Qdrant vector store using a host name and port.
    /// </summary>
    /// <param name="host">The Qdrant host name (without scheme or port).</param>
    /// <param name="port">The Qdrant port (default: 6333).</param>
    /// <param name="collectionName">The collection name to use (default: "ragify_vectors").</param>
    /// <param name="vectorSize">The dimensionality of stored vectors (default: 1536).</param>
    /// <param name="useHttps">Whether to use HTTPS when connecting (default: false).</param>
    /// <param name="apiKey">Optional API key for authenticated Qdrant instances.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithQdrantVectorStore(
        string host,
        int port = 6333,
        string collectionName = "ragify_vectors",
        int vectorSize = 1536,
        bool useHttps = false,
        string? apiKey = null,
        HttpClient? httpClient = null)
    {
        _vectorStore = new QdrantVectorStore(host, port, collectionName, vectorSize, useHttps, apiKey, httpClient);
        return this;
    }

    /// <summary>
    /// Configures a Qdrant vector store using a fully-qualified base URL.
    /// </summary>
    /// <param name="baseUrl">The Qdrant base URL (including scheme and port).</param>
    /// <param name="collectionName">The collection name to use.</param>
    /// <param name="vectorSize">The dimensionality of stored vectors (default: 1536).</param>
    /// <param name="apiKey">Optional API key for authenticated Qdrant instances.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithQdrantVectorStore(
        string baseUrl,
        string collectionName,
        int vectorSize = 1536,
        string? apiKey = null,
        HttpClient? httpClient = null)
    {
        _vectorStore = new QdrantVectorStore(baseUrl, collectionName, vectorSize, apiKey, httpClient);
        return this;
    }

    #endregion

    #region PgVector

    /// <summary>
    /// Configures a PostgreSQL (pgvector) backed vector store.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name to use (default: "ragify_vectors").</param>
    /// <param name="vectorSize">The dimensionality of stored vectors (default: 1536).</param>
    /// <param name="options">Optional pgvector store options. If not provided, defaults will be used.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithPgVectorStore(
        string connectionString,
        string tableName = "ragify_vectors",
        int vectorSize = 1536,
        PgVectorStoreOptions? options = null)
    {
        _vectorStore = new PgVectorStore(connectionString, tableName, vectorSize, options);
        return this;
    }

    #endregion

    #region Pinecone

    /// <summary>
    /// Configures a Pinecone vector store using an index name and environment.
    /// </summary>
    /// <param name="apiKey">The Pinecone API key.</param>
    /// <param name="indexName">The Pinecone index name.</param>
    /// <param name="environment">The Pinecone environment (e.g., "us-east-1-aws").</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithPineconeVectorStore(
        string apiKey,
        string indexName,
        string environment,
        HttpClient? httpClient = null)
    {
        _vectorStore = new PineconeVectorStore(apiKey, indexName, environment, httpClient);
        return this;
    }

    /// <summary>
    /// Configures a Pinecone vector store using an explicit base URL.
    /// </summary>
    /// <param name="apiKey">The Pinecone API key.</param>
    /// <param name="indexName">The Pinecone index name.</param>
    /// <param name="baseUrl">The fully-qualified Pinecone index base URL.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithPineconeVectorStore(
        string apiKey,
        string indexName,
        Uri baseUrl,
        HttpClient? httpClient = null)
    {
        _vectorStore = new PineconeVectorStore(apiKey, indexName, baseUrl, httpClient);
        return this;
    }

    #endregion

    #region Weaviate

    /// <summary>
    /// Configures a Weaviate vector store.
    /// </summary>
    /// <param name="baseUrl">The Weaviate base URL (including scheme and port).</param>
    /// <param name="className">The Weaviate class name to use (default: "RAGifyVector").</param>
    /// <param name="apiKey">Optional API key for authenticated Weaviate instances.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>. If not provided, a new one will be created.</param>
    /// <returns>The config instance for method chaining.</returns>
    public RagifyConfig WithWeaviateVectorStore(
        string baseUrl,
        string className = "RAGifyVector",
        string? apiKey = null,
        HttpClient? httpClient = null)
    {
        _vectorStore = new WeaviateVectorStore(baseUrl, className, apiKey, httpClient);
        return this;
    }

    #endregion

    #endregion
}
