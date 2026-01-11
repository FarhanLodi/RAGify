namespace RAGify.Core;

/// <summary>
/// Provides mathematical operations for vector calculations.
/// </summary>
public static class VectorMath
{
    #region Public-Methods

    /// <summary>
    /// Calculates the cosine similarity between two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The cosine similarity value between 0.0 and 1.0.</returns>
    /// <exception cref="ArgumentException">Thrown when vectors have different dimensions.</exception>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimension.");

        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0.0 || normB == 0.0)
            return 0.0;

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>
    /// Normalizes a vector to unit length (L2 normalization).
    /// </summary>
    /// <param name="vector">The vector to normalize.</param>
    /// <returns>A new normalized vector with the same direction but unit length.</returns>
    public static float[] Normalize(float[] vector)
    {
        double norm = 0.0;
        for (int i = 0; i < vector.Length; i++)
        {
            norm += vector[i] * vector[i];
        }
        norm = Math.Sqrt(norm);

        if (norm == 0.0)
            return (float[])vector.Clone();

        float[] normalized = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            normalized[i] = (float)(vector[i] / norm);
        }

        return normalized;
    }

    #endregion
}
