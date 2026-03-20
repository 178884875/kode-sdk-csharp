namespace KodaClaw.Contracts;

public interface IThreadBindingRepository
{
    Task UpsertAsync(ThreadBinding binding, CancellationToken cancellationToken = default);

    Task<ThreadBinding?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<ThreadBinding?> GetByExternalThreadAsync(
        ChannelConnectorKind connectorKind,
        string accountId,
        string externalThreadId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThreadBinding>> ListAsync(
        ChannelQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> UpdateDeliveryModeOverrideAsync(
        string id,
        DeliveryMode? deliveryModeOverride,
        CancellationToken cancellationToken = default);
}
