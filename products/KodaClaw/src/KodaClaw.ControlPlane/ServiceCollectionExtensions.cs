using KodaClaw.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KodaClaw.ControlPlane;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawControlPlane(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICorrelationContextAccessor, AsyncLocalCorrelationContextAccessor>();
        services.TryAddSingleton<IDiagnosticsService, InMemoryDiagnosticsService>();
        services.TryAddSingleton<IInboxRepository, SqliteInboxRepository>();
        services.TryAddSingleton<IApprovalRepository, SqliteApprovalRepository>();
        services.TryAddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        return services;
    }
}
