namespace RAGify.VectorStores;

/// <summary>
/// Configuration options for PgVectorStore including customizable SQL queries.
/// </summary>
public class PgVectorStoreOptions
{
    #region Public-Members

    /// <summary>
    /// Gets or sets the SQL query template for upserting a single vector.
    /// Placeholders: {tableName} will be replaced with the actual table name.
    /// </summary>
    public string UpsertQuery { get; set; } = @"
            INSERT INTO {tableName} (vector_id, embedding, metadata)
            VALUES (@vectorId, @embedding::vector, @metadata::jsonb)
            ON CONFLICT (vector_id) 
            DO UPDATE SET embedding = @embedding::vector, metadata = @metadata::jsonb";

    /// <summary>
    /// Gets or sets the SQL query template for deleting a vector by ID.
    /// Placeholders: {tableName} will be replaced with the actual table name.
    /// </summary>
    public string DeleteByIdQuery { get; set; } = "DELETE FROM {tableName} WHERE vector_id = @vectorId";

    /// <summary>
    /// Gets or sets the SQL query template for deleting vectors by document ID.
    /// Placeholders: {tableName} will be replaced with the actual table name.
    /// </summary>
    public string DeleteByDocumentIdQuery { get; set; } = "DELETE FROM {tableName} WHERE metadata->>'DocumentId' = @documentId";

    /// <summary>
    /// Gets or sets the SQL query template for searching similar vectors.
    /// Placeholders: {tableName} will be replaced with the actual table name, {whereClause} will be replaced with filter conditions.
    /// </summary>
    public string SearchQuery { get; set; } = @"
            SELECT vector_id, 
                   1 - (embedding <=> @queryVector::vector) as similarity,
                   metadata
            FROM {tableName}
            WHERE {whereClause}
            AND (1 - (embedding <=> @queryVector::vector)) >= @threshold
            ORDER BY embedding <=> @queryVector::vector
            LIMIT @topK";

    /// <summary>
    /// Gets or sets the SQL query template for clearing all vectors.
    /// Placeholders: {tableName} will be replaced with the actual table name.
    /// </summary>
    public string ClearQuery { get; set; } = "TRUNCATE TABLE {tableName}";

    /// <summary>
    /// Gets or sets the SQL query template for getting the count of vectors.
    /// Placeholders: {tableName} will be replaced with the actual table name.
    /// </summary>
    public string CountQuery { get; set; } = "SELECT COUNT(*) FROM {tableName}";

    /// <summary>
    /// Gets or sets the SQL query template for checking if a table exists.
    /// Placeholders: {tableName} will be replaced with the actual table name.
    /// </summary>
    public string CheckTableExistsQuery { get; set; } = @"
                SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_name = '{tableName}'
                )";

    /// <summary>
    /// Gets or sets the SQL query for creating the pgvector extension.
    /// </summary>
    public string CreateExtensionQuery { get; set; } = "CREATE EXTENSION IF NOT EXISTS vector";

    /// <summary>
    /// Gets or sets the SQL query template for creating the table.
    /// Placeholders: {tableName} will be replaced with the actual table name, {vectorSize} will be replaced with the vector size.
    /// </summary>
    public string CreateTableQuery { get; set; } = @"
                    CREATE TABLE IF NOT EXISTS {tableName} (
                        vector_id TEXT PRIMARY KEY,
                        embedding vector({vectorSize}),
                        metadata JSONB
                    )";

    /// <summary>
    /// Gets or sets the SQL query template for creating the index.
    /// Placeholders: {tableName} will be replaced with the actual table name.
    /// </summary>
    public string CreateIndexQuery { get; set; } = @"
                    CREATE INDEX IF NOT EXISTS {tableName}_embedding_idx 
                    ON {tableName} 
                    USING ivfflat (embedding vector_cosine_ops)";

    /// <summary>
    /// Gets or sets the template for filter conditions in the WHERE clause.
    /// Placeholders: {key} will be replaced with the metadata key, {index} will be replaced with the parameter index.
    /// </summary>
    public string FilterConditionTemplate { get; set; } = "metadata->>'{key}' = @filterValue{index}";

    /// <summary>
    /// Gets or sets the default WHERE clause when no filters are applied.
    /// </summary>
    public string DefaultWhereClause { get; set; } = "1=1";

    #endregion

    #region Public-Methods

    /// <summary>
    /// Replaces placeholders in a query template with actual values.
    /// </summary>
    /// <param name="query">The query template.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="vectorSize">The vector size (optional).</param>
    /// <param name="whereClause">The WHERE clause (optional).</param>
    /// <returns>The query with placeholders replaced.</returns>
    public string FormatQuery(string query, string tableName, int? vectorSize = null, string? whereClause = null)
    {
        var result = query.Replace("{tableName}", tableName);
        
        if (vectorSize.HasValue)
        {
            result = result.Replace("{vectorSize}", vectorSize.Value.ToString());
        }
        
        if (whereClause != null)
        {
            result = result.Replace("{whereClause}", whereClause);
        }
        
        return result;
    }

    #endregion
}

