using KodaClaw.Contracts;
using KodaClaw.ChannelHub.Connectors.Telegram;
using KodaClaw.ChannelHub.Connectors.Webhook;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KodaClaw.ChannelHub;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawChannelHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IChannelAccountRepository, SqliteChannelAccountRepository>();
        services.TryAddSingleton<IThreadBindingRepository, SqliteThreadBindingRepository>();
        services.TryAddSingleton<IChannelAuditRepository, SqliteChannelAuditRepository>();
        services.TryAddSingleton<ChannelAuditQueryService>();
        services.TryAddSingleton<ChannelPolicyEngine>();
        services.TryAddSingleton<ChannelEventIngestionService>();
        services.TryAddSingleton<ChannelDeliveryGovernanceService>();
        services.TryAddSingleton<ChannelDeliveryDispatchService>();
        services.TryAddSingleton<ChannelDeliveryApprovalService>();
        services.TryAddSingleton<IChannelThreadSummaryWriter, ChannelThreadSummaryWriter>();
        services.TryAddSingleton<ChannelTurnOrchestrator>();
        services.TryAddSingleton<ITelegramApiClient, HttpTelegramApiClient>();
        services.TryAddSingleton<TelegramConnector>();
        services.TryAddSingleton<GenericWebhookConnector>();
        return services;
    }
}
