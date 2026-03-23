using Kode.Agent.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KodaClaw.McpHub;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawMcpHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMcpClientManager();
        services.TryAddSingleton<IMcpHubService, McpHubService>();
        return services;
    }
}
