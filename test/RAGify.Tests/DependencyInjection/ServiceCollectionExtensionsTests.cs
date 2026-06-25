using Microsoft.Extensions.DependencyInjection;
using RAGify;
using RAGify.Abstractions;

namespace RAGify.Tests;

/// <summary>
/// Tests for the <c>AddRagify</c> dependency injection extension methods.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    #region Tests

    /// <summary>
    /// Verifies that <c>AddRagify</c> registers a resolvable <see cref="IRagify"/> singleton.
    /// </summary>
    [Fact]
    public void AddRagify_RegistersResolvableIRagify()
    {
        var services = new ServiceCollection();

        services.AddRagify(cfg => cfg
            .WithChunking(ChunkingStrategyType.FixedSize)
            .WithEmbeddings(new FakeEmbeddingProvider())
            .WithInMemoryVectorStore());

        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IRagify>());
    }

    /// <summary>
    /// Verifies that configuring a Weaviate vector store does not throw at configuration/build time.
    /// </summary>
    [Fact]
    public void WithWeaviateVectorStore_DoesNotThrowAtConfigurationTime()
    {
        var exception = Record.Exception(() =>
        {
            var config = new RagifyConfig()
                .WithChunking(ChunkingStrategyType.FixedSize)
                .WithEmbeddings(new FakeEmbeddingProvider())
                .WithWeaviateVectorStore("http://localhost:8080");

            _ = config.Build();
        });

        Assert.Null(exception);
    }

    #endregion

    #region Test-Doubles

    /// <summary>
    /// A minimal <see cref="IEmbeddingProvider"/> that returns fixed-length zero vectors.
    /// </summary>
    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        /// <inheritdoc />
        public int Dimension => 4;

        /// <inheritdoc />
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new float[Dimension]);

        /// <inheritdoc />
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<float[]> result = texts.Select(_ => new float[Dimension]).ToList();
            return Task.FromResult(result);
        }
    }

    #endregion
}
