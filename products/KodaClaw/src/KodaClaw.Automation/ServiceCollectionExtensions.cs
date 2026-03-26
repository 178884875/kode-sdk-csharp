using KodaClaw.Contracts;
using KodaClaw.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KodaClaw.Automation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawAutomation(
        this IServiceCollection services,
        Action<AutomationSchedulerOptions>? configureScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var schedulerOptions = new AutomationSchedulerOptions();
        configureScheduler?.Invoke(schedulerOptions);
        if (schedulerOptions.PollInterval <= TimeSpan.Zero)
        {
            schedulerOptions.PollInterval = TimeSpan.FromMinutes(1);
        }

        if (schedulerOptions.FailureRetryDelay <= TimeSpan.Zero)
        {
            schedulerOptions.FailureRetryDelay = TimeSpan.FromMinutes(15);
        }

        services.TryAddSingleton(schedulerOptions);
        services.TryAddSingleton<SqliteAutomationDatabase>();
        services.TryAddSingleton<IAutomationDefinitionRepository>(provider =>
            new SqliteAutomationDefinitionRepository(provider.GetRequiredService<SqliteAutomationDatabase>()));
        services.TryAddSingleton<IAutomationRunRepository>(provider =>
            new SqliteAutomationRunRepository(provider.GetRequiredService<SqliteAutomationDatabase>()));
        services.TryAddSingleton<IAutomationClock, SystemAutomationClock>();
        services.TryAddSingleton<IAutomationScheduler>(provider =>
        {
            var sessionService = provider.GetService<IAutomationSessionService>();
            var inboxRepository = provider.GetService<IInboxRepository>();
            if (sessionService is null || inboxRepository is null)
            {
                return new DisabledAutomationScheduler();
            }

            return new AutomationScheduler(
                provider.GetRequiredService<IAutomationDefinitionRepository>(),
                provider.GetRequiredService<IAutomationRunRepository>(),
                sessionService,
                inboxRepository,
                provider.GetRequiredService<IAutomationClock>(),
                provider.GetRequiredService<AutomationSchedulerOptions>(),
                provider.GetService<ISettingsRepository>(),
                provider.GetService<IAutomationNotificationService>(),
                provider.GetService<IMemoryConsolidationService>(),
                provider.GetService<ILogger<AutomationScheduler>>(),
                provider.GetService<IHostApplicationLifetime>());
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AutomationSchedulerHostedService>());
        return services;
    }

    private sealed class DisabledAutomationScheduler : IAutomationScheduler
    {
        public Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }

        public Task<int> TickAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }

        public Task<string?> TriggerDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }
    }
}
