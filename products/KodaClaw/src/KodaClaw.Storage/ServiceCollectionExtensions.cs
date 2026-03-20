using KodaClaw.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KodaClaw.Storage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICanvasArtifactRepository, SqliteCanvasArtifactRepository>();
        return services;
    }
}
