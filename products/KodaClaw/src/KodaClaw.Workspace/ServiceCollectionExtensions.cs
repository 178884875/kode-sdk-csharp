using KodaClaw.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KodaClaw.Workspace;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawWorkspace(
        this IServiceCollection services,
        Action<KodaClawWorkspaceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new KodaClawWorkspaceOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IMacOsKeychainCommandRunner, MacOsKeychainCommandRunner>();
        services.TryAddSingleton<ISecretStore, PlatformSecretStore>();
        services.TryAddSingleton<IWorkspaceService, WorkspaceService>();
        services.TryAddSingleton<IMediaStore, LocalMediaStore>();
        services.TryAddSingleton<IWorkspaceReadinessService, WorkspaceReadinessService>();
        services.TryAddSingleton<IBootstrapService, BootstrapService>();
        services.TryAddSingleton<IHeartbeatAutomationCompiler, HeartbeatAutomationCompiler>();
        services.TryAddSingleton<IHeartbeatSyncService>(provider => new HeartbeatSyncService(
            provider.GetRequiredService<IWorkspaceService>(),
            provider.GetRequiredService<IHeartbeatAutomationCompiler>(),
            provider.GetRequiredService<IAutomationDefinitionRepository>(),
            provider.GetService<ILogger<HeartbeatSyncService>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HeartbeatFileWatcherHostedService>());
        return services;
    }
}
