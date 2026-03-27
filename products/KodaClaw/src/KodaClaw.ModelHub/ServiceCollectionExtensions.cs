using KodaClaw.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KodaClaw.ModelHub;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddModelRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IGenerationService, OpenAIImageGenerationService>();
        services.TryAddSingleton<ISpeechService, OpenAICompatibleTtsService>();
        return services;
    }
}
