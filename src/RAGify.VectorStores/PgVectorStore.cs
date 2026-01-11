using Npgsql;
using RAGify.Abstractions;
using RAGify.Core;

namespace RAGify.VectorStores;

/// <summary>
/// PostgreSQL with pgvector extension implementation of IVectorStore.
/// </summary>
public class PgVectorStore : IVectorStore
{
    #region Private-Members

    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly int _vectorSize;
    private readonly PgVectorStoreOptions _options;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the PgVectorStore class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The name of the table to use.</param>
    /// <param name="vectorSize">The size of the vectors.</param>
    /// <param name="options">Optional configuration for customizing SQL queries. If not provided, default queries will be used.</param>
    public PgVectorStore(string connectionString, string tableName = "ragify_vectors", int vectorSize = 1536, PgVectorStoreOptions? options = null)
    {
        _connectionString = connectionString;
        _tableName = tableName;
        _vectorSize = vectorSize;
        _options = options ?? new PgVectorStoreOptions();
    }

    #endregion

    #region Public-Methods

    /// <summary>
    /// Upserts a single vector into the store.
    /// </summary>
    public async Task UpsertAsync(string vectorId, float[] vector, IReadOnlyDictionary<string, object> metadata, CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken);

        var normalized = VectorMath.Normalize(vector);
        var vectorString = string.Join(",", normalized.Select(v => v.ToString("F6")));
        var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = _options.FormatQuery(_options.UpsertQuery, _tableName);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("vectorId", vectorId);
        command.Parameters.AddWithValue("embedding", $"[{vectorString}]");
        command.Parameters.AddWithValue("metadata", metadataJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Upserts multiple vectors into the store in batch.
    /// </summary>
    public async Task UpsertBatchAsync(IReadOnlyList<VectorData> vectors, CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var vectorData in vectors)
            {
                var normalized = VectorMath.Normalize(vectorData.Vector);
                var vectorString = string.Join(",", normalized.Select(v => v.ToString("F6")));
                var metadataJson = System.Text.Json.JsonSerializer.Serialize(vectorData.Metadata);

                var sql = _options.FormatQuery(_options.UpsertQuery, _tableName);

                await using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("vectorId", vectorData.VectorId);
                command.Parameters.AddWithValue("embedding", $"[{vectorString}]");
                command.Parameters.AddWithValue("metadata", metadataJson);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Deletes a vector from the store by its ID.
    /// </summary>
    public async Task DeleteAsync(string vectorId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = _options.FormatQuery(_options.DeleteByIdQuery, _tableName);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("vectorId", vectorId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes all vectors associated with a specific document ID.
    /// </summary>
    public async Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = _options.FormatQuery(_options.DeleteByDocumentIdQuery, _tableName);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("documentId", documentId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Searches for similar vectors using cosine similarity.
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        double threshold = 0.0,
        MetadataFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken);

        var normalizedQuery = VectorMath.Normalize(queryVector);
        var queryVectorString = string.Join(",", normalizedQuery.Select(v => v.ToString("F6")));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var whereClause = _options.DefaultWhereClause;
        if (filter != null && filter.Filters.Count > 0)
        {
            var conditions = filter.Filters.Select((kvp, index) => 
                _options.FilterConditionTemplate
                    .Replace("{key}", kvp.Key)
                    .Replace("{index}", index.ToString()));
            whereClause = string.Join(" AND ", conditions);
        }

        var sql = _options.FormatQuery(_options.SearchQuery, _tableName, whereClause: whereClause);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("queryVector", $"[{queryVectorString}]");
        command.Parameters.AddWithValue("threshold", threshold);
        command.Parameters.AddWithValue("topK", topK);

        if (filter != null)
        {
            var index = 0;
            foreach (var kvp in filter.Filters)
            {
                command.Parameters.AddWithValue($"filterValue{index}", kvp.Value.ToString() ?? string.Empty);
                index++;
            }
        }

        var results = new List<VectorSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var vectorId = reader.GetString(0);
            var similarity = reader.GetDouble(1);
            var metadataJson = reader.GetString(2);
            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson) 
                ?? new Dictionary<string, object>();

            results.Add(new VectorSearchResult
            {
                VectorId = vectorId,
                Similarity = similarity,
                Metadata = metadata
            });
        }

        return results;
    }

    /// <summary>
    /// Clears all vectors from the store.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = _options.FormatQuery(_options.ClearQuery, _tableName);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the total count of vectors stored in the store.
    /// </summary>
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = _options.FormatQuery(_options.CountQuery, _tableName);
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result != null ? Convert.ToInt32(result) : 0;
    }

    #endregion

    #region Private-Methods

    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await _initSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Check if table exists
            var checkTableSql = _options.FormatQuery(_options.CheckTableExistsQuery, _tableName);

            await using var checkCommand = new NpgsqlCommand(checkTableSql, connection);
            var tableExists = (bool)(await checkCommand.ExecuteScalarAsync(cancellationToken) ?? false);

            if (!tableExists)
            {
                // Create extension if it doesn't exist
                await using var extCommand = new NpgsqlCommand(_options.CreateExtensionQuery, connection);
                await extCommand.ExecuteNonQueryAsync(cancellationToken);

                // Create table
                var createTableSql = _options.FormatQuery(_options.CreateTableQuery, _tableName, _vectorSize);

                await using var createCommand = new NpgsqlCommand(createTableSql, connection);
                await createCommand.ExecuteNonQueryAsync(cancellationToken);

                // Create index for faster similarity search
                var createIndexSql = _options.FormatQuery(_options.CreateIndexQuery, _tableName);

                await using var indexCommand = new NpgsqlCommand(createIndexSql, connection);
                await indexCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    #endregion
}

