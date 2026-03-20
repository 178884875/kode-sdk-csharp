using KodaClaw.Contracts;
using KodaClaw.PluginHost.Diagnostics;
using KodaClaw.PluginHost.Hosting;
using KodaClaw.PluginHost.Manifest;
using KodaClaw.PluginHost.Registry;
using KodaClaw.PluginHost.Trust;
using Kode.Agent.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KodaClaw.PluginHost;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawPluginHost(
        this IServiceCollection services,
        Action<PluginHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PluginHostOptions();
        configure?.Invoke(options);
        if (options.MaxRestartAttempts < 0)
        {
            options.MaxRestartAttempts = 0;
        }

        if (options.DefaultHealthcheckTimeoutSeconds <= 0)
        {
            options.DefaultHealthcheckTimeoutSeconds = 5;
        }

        services.AddMcpClientManager();
        services.TryAddSingleton(options);
        services.TryAddSingleton<PluginManifestLoader>();
        services.TryAddSingleton<IPluginTrustEvaluator, PluginTrustEvaluator>();
        services.TryAddSingleton<IPluginRegistryRepository, SqlitePluginRegistryRepository>();
        services.TryAddSingleton<IPluginLogRepository, SqlitePluginLogRepository>();
        services.TryAddSingleton<IPluginLifecycleHost, PluginLifecycleHost>();
        return services;
    }
}
