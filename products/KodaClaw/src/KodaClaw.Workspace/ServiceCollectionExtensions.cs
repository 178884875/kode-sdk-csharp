using KodaClaw.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton<IBootstrapService, BootstrapService>();
        services.TryAddSingleton<IHeartbeatAutomationCompiler, HeartbeatAutomationCompiler>();
        return services;
    }
}
