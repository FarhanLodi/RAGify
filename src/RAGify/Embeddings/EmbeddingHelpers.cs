using RAGify.Core;

namespace RAGify.Embeddings;

/// <summary>
/// Helper methods for embedding operations, including text splitting and embedding averaging.
/// </summary>
internal static class EmbeddingHelpers
{
    /// <summary>
    /// Splits text into smaller chunks at word boundaries.
    /// </summary>
    public static List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        var words = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        
        var currentChunk = new System.Text.StringBuilder();
        
        foreach (var word in words)
        {
            var potentialLength = currentChunk.Length + word.Length + 1; // +1 for space
            
            if (potentialLength > maxChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }
            
            if (currentChunk.Length > 0)
                currentChunk.Append(' ');
            
            currentChunk.Append(word);
        }
        
        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString().Trim());
        
        return chunks;
    }

    /// <summary>
    /// Averages multiple embeddings into a single embedding vector.
    /// </summary>
    public static float[] AverageEmbeddings(List<float[]> embeddings)
    {
        if (embeddings.Count == 0)
            throw new ArgumentException("Cannot average empty list of embeddings.", nameof(embeddings));
        
        if (embeddings.Count == 1)
            return embeddings[0];
        
        var dimension = embeddings[0].Length;
        var averaged = new float[dimension];
        
        foreach (var embedding in embeddings)
        {
            if (embedding.Length != dimension)
                throw new ArgumentException("All embeddings must have the same dimension.", nameof(embeddings));
                
            for (int i = 0; i < dimension; i++)
            {
                averaged[i] += embedding[i];
            }
        }
        
        var count = (float)embeddings.Count;
        for (int i = 0; i < dimension; i++)
        {
            averaged[i] /= count;
        }
        
        return VectorMath.Normalize(averaged);
    }
}

