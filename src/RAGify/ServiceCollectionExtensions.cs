using RAGify;
using RAGify.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering RAGify services with a <see cref="IServiceCollection"/>.
/// </summary>
public static class RagifyServiceCollectionExtensions
{
    #region Public-Methods

    /// <summary>
    /// Registers a configured <see cref="IRagify"/> instance as a singleton.
    /// </summary>
    /// <param name="services">The service collection to add the registration to.</param>
    /// <param name="configure">A delegate used to configure the RAGify pipeline.</param>
    /// <returns>The same service collection so that calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    public static IServiceCollection AddRagify(this IServiceCollection services, Action<RagifyConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var config = new RagifyConfig();
        configure(config);
        var ragify = config.Build();

        services.AddSingleton<IRagify>(ragify);
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IRagify"/> singleton whose configuration is resolved from the service provider.
    /// </summary>
    /// <param name="services">The service collection to add the registration to.</param>
    /// <param name="configure">A delegate that produces a <see cref="RagifyConfig"/> from the service provider.</param>
    /// <returns>The same service collection so that calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    public static IServiceCollection AddRagify(this IServiceCollection services, Func<IServiceProvider, RagifyConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton<IRagify>(sp => configure(sp).Build());
        return services;
    }

    #endregion
}
