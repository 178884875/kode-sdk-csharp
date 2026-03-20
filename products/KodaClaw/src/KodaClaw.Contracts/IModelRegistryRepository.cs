namespace KodaClaw.Contracts;

public interface IModelRegistryRepository
{
    Task<IReadOnlyList<ModelEndpoint>> ListAsync(CancellationToken cancellationToken = default);

    Task<ModelEndpoint?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task AddAsync(ModelEndpoint endpoint, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(ModelEndpoint endpoint, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> SetDefaultAsync(string id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);
}
