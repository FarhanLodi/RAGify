using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RAGify.Abstractions;
using RAGify.Core;
using System.Text;

namespace RAGify.Embeddings;

/// <summary>
/// ONNX embedding provider for local ONNX models (e.g., SentenceTransformer models).
/// Requires tokenization - users should provide pre-tokenized input or use a tokenizer library.
/// </summary>
public class OnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    #region Private-Members

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly int _dimension;
    private readonly Func<string, int[]>? _tokenizer;

    #endregion

    #region Public-Members

    /// <summary>
    /// Gets the dimension of vectors produced by this provider.
    /// </summary>
    public int Dimension => _dimension;

    #endregion

    /// <summary>
    /// Initializes a new instance of the OnnxEmbeddingProvider.
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model file.</param>
    /// <param name="dimension">Vector dimension. If not specified, will be inferred from model output.</param>
    /// <param name="inputName">Name of the input tensor (default: "input_ids").</param>
    /// <param name="outputName">Name of the output tensor (default: "last_hidden_state" or "embeddings").</param>
    /// <param name="tokenizer">Optional tokenizer function. If not provided, text will be split by whitespace.</param>
    /// <param name="sessionOptions">Optional ONNX Runtime session options.</param>
    /// <exception cref="FileNotFoundException">Thrown when the model file is not found.</exception>
    public OnnxEmbeddingProvider(
        string modelPath,
        int? dimension = null,
        string? inputName = null,
        string? outputName = null,
        Func<string, int[]>? tokenizer = null,
        Microsoft.ML.OnnxRuntime.SessionOptions? sessionOptions = null)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"ONNX model file not found: {modelPath}");

        _session = new InferenceSession(modelPath, sessionOptions ?? new SessionOptions());
        _tokenizer = tokenizer;
        
        _inputName = inputName ?? _session.InputMetadata.Keys.FirstOrDefault() ?? "input_ids";
        _outputName = outputName ?? _session.OutputMetadata.Keys.FirstOrDefault() ?? "last_hidden_state";

        if (dimension.HasValue)
        {
            _dimension = dimension.Value;
        }
        else
        {
            if (_session.OutputMetadata.TryGetValue(_outputName, out var outputMetadata))
            {
                var dimensions = outputMetadata.Dimensions;
                _dimension = dimensions.Length >= 2 ? (int)dimensions[dimensions.Length - 1] : 768;
            }
            else
            {
                _dimension = 768;
            }
        }
    }

    #region Public-Methods

    /// <summary>
    /// Generates an embedding vector for the specified text using the ONNX model.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the normalized embedding vector.</returns>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        try
        {
            return await EmbedAsyncInternal(text, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("shape") || ex.Message.Contains("dimension"))
        {
            // ONNX models may fail with shape errors if input is too large - split and retry
            return await EmbedWithSplittingAsync(text, cancellationToken);
        }
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts in batch using the ONNX model.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of normalized embedding vectors.</returns>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts == null || texts.Count == 0)
            return Array.Empty<float[]>();

        return await Task.Run(() =>
        {
            var embeddings = new List<float[]>();
            
            foreach (var text in texts)
            {
                var inputIds = Tokenize(text);
                var shape = new[] { 1, inputIds.Length };
                var inputTensor = new DenseTensor<long>(inputIds.Select(i => (long)i).ToArray(), shape);
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
                };

                using var results = _session.Run(inputs);
                var output = results.FirstOrDefault(r => r.Name == _outputName);
                
                if (output == null)
                    throw new InvalidOperationException($"Output tensor '{_outputName}' not found in model output.");

                var tensor = output.AsTensor<float>();
                var embedding = ExtractEmbedding(tensor);
                embeddings.Add(VectorMath.Normalize(embedding));
            }

            return embeddings;
        }, cancellationToken);
    }

    /// <summary>
    /// Disposes the ONNX inference session.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }

    #endregion

    #region Private-Methods

    private int[] Tokenize(string text)
    {
        if (_tokenizer != null)
            return _tokenizer(text);

        return Encoding.UTF8.GetBytes(text).Take(512).Select(b => (int)b).ToArray();
    }

    private float[] ExtractEmbedding(Tensor<float> tensor)
    {
        var shape = tensor.Dimensions.ToArray();
        
        if (shape.Length == 3)
        {
            var batchSize = shape[0];
            var seqLen = shape[1];
            var hiddenSize = shape[2];
            var embedding = new float[hiddenSize];

            for (int i = 0; i < hiddenSize; i++)
            {
                float sum = 0;
                for (int j = 0; j < seqLen; j++)
                {
                    sum += tensor[0, j, i];
                }
                embedding[i] = sum / seqLen;
            }
            return embedding;
        }
        else if (shape.Length == 2)
        {
            var hiddenSize = shape[1];
            var embedding = new float[hiddenSize];
            for (int i = 0; i < hiddenSize; i++)
            {
                embedding[i] = tensor[0, i];
            }
            return embedding;
        }
        else
        {
            throw new InvalidOperationException($"Unexpected tensor shape: [{string.Join(", ", shape)}]");
        }
    }

    /// <summary>
    /// Internal embedding method that handles context length errors by splitting.
    /// </summary>
    private async Task<float[]> EmbedAsyncInternal(string text, CancellationToken cancellationToken, int recursionDepth = 0)
    {
        if (recursionDepth > 5)
        {
            throw new InvalidOperationException(
                $"Text chunk is too large even after multiple splits. " +
                $"Original text length: {text.Length} characters. " +
                $"Please reduce the chunk size in your ChunkingOptions.");
        }

        return await Task.Run(() =>
        {
            var inputIds = Tokenize(text);
            
            // Check if input exceeds typical ONNX model limits (512 tokens is common)
            if (inputIds.Length > 512 && recursionDepth == 0)
            {
                throw new InvalidOperationException($"Input length ({inputIds.Length} tokens) exceeds model context length. Splitting...");
            }
            
            var shape = new[] { 1, inputIds.Length };
            var inputTensor = new DenseTensor<long>(inputIds.Select(i => (long)i).ToArray(), shape);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
            };

            using var results = _session.Run(inputs);
            var output = results.FirstOrDefault(r => r.Name == _outputName);
            
            if (output == null)
                throw new InvalidOperationException($"Output tensor '{_outputName}' not found in model output.");

            var tensor = output.AsTensor<float>();
            var embedding = ExtractEmbedding(tensor);
            
            return VectorMath.Normalize(embedding);
        }, cancellationToken);
    }

    /// <summary>
    /// Splits text that exceeds context length into smaller chunks and averages their embeddings.
    /// </summary>
    private async Task<float[]> EmbedWithSplittingAsync(string text, CancellationToken cancellationToken, int recursionDepth = 0)
    {
        var maxChunkSize = Math.Max(100, text.Length / 2);
        var chunks = EmbeddingHelpers.SplitTextIntoChunks(text, maxChunkSize);
        
        if (chunks.Count == 0)
            throw new InvalidOperationException("Failed to split text into chunks.");

        var embeddings = new List<float[]>();
        
        foreach (var chunk in chunks)
        {
            try
            {
                var embedding = await EmbedAsyncInternal(chunk, cancellationToken, recursionDepth + 1);
                embeddings.Add(embedding);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                                                       ex.Message.Contains("shape", StringComparison.OrdinalIgnoreCase) ||
                                                       ex.Message.Contains("dimension", StringComparison.OrdinalIgnoreCase) ||
                                                       ex.Message.Contains("exceeds", StringComparison.OrdinalIgnoreCase))
            {
                var subEmbedding = await EmbedWithSplittingAsync(chunk, cancellationToken, recursionDepth + 1);
                embeddings.Add(subEmbedding);
            }
        }

        return EmbeddingHelpers.AverageEmbeddings(embeddings);
    }

    #endregion
}
