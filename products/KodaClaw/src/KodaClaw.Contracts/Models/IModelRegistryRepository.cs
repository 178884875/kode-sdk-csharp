namespace KodaClaw.Contracts;

public interface IModelRegistryRepository
{
    Task<IReadOnlyList<ModelEndpoint>> ListAsync(CancellationToken cancellationToken = default);

    Task<ModelEndpoint?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task AddAsync(ModelEndpoint endpoint, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(ModelEndpoint endpoint, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> SetDefaultAsync(string id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 返回第一个 enabled 且 Capabilities 包含所有 required 能力的 endpoint，
    /// is_default=true 的优先，无匹配时返回 null。
    /// </summary>
    Task<ModelEndpoint?> ResolveDefaultForAsync(ModelCapabilitySet required, CancellationToken cancellationToken = default);
}
