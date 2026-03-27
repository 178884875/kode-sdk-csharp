using KodaClaw.Contracts;
using KodaClaw.Storage;

namespace KodaClaw.Storage.Json.Repositories;

/// <summary>
/// ModelEndpoint 的 JSON 文件存储实现。
/// 路径：{workspaceRoot}/config/models/{id}.json
/// </summary>
public sealed class JsonModelRegistryRepository : JsonStoreBase, IModelRegistryRepository
{
    private readonly string _dir;

    public JsonModelRegistryRepository(string workspaceRoot)
    {
        _dir = Path.Combine(workspaceRoot, "config", "models");
    }

    public Task<IReadOnlyList<ModelEndpoint>> ListAsync(CancellationToken cancellationToken = default)
        => ScanAndSortAsync(cancellationToken);

    public Task<ModelEndpoint?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => ReadEntityAsync<ModelEndpoint>(FilePath(id), cancellationToken);

    public Task AddAsync(ModelEndpoint endpoint, CancellationToken cancellationToken = default)
        => WriteEntityAsync(FilePath(endpoint.Id), endpoint, cancellationToken);

    public async Task<bool> UpdateAsync(ModelEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath(endpoint.Id))) return false;
        await WriteEntityAsync(FilePath(endpoint.Id), endpoint, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await DeleteEntityAsync(FilePath(id), cancellationToken);
        return true;
    }

    public async Task<bool> SetDefaultAsync(string id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var models = await ScanDirectoryAsync<ModelEndpoint>(_dir, null, cancellationToken);
        if (!models.Any(m => m.Id == id)) return false;

        // 更新所有 endpoint 的 IsDefault 标志，原子写回每个文件
        foreach (var m in models)
        {
            var updated = m with
            {
                IsDefault = m.Id == id,
                UpdatedAt = m.Id == id ? updatedAt : m.UpdatedAt,
            };
            await WriteEntityAsync(FilePath(m.Id), updated, cancellationToken);
        }
        return true;
    }

    public async Task<ModelEndpoint?> ResolveDefaultForAsync(
        ModelCapabilitySet required,
        CancellationToken cancellationToken = default)
    {
        var models = await ScanDirectoryAsync<ModelEndpoint>(_dir, null, cancellationToken);
        return models
            .Where(m => m.Enabled && (m.Capabilities & required) == required)
            .OrderByDescending(m => m.IsDefault)
            .ThenBy(m => m.CreatedAt)
            .FirstOrDefault();
    }

    // ─── 私有工具 ──────────────────────────────────────────────────────────────

    private string FilePath(string id) => Path.Combine(_dir, $"{id}.json");

    private async Task<IReadOnlyList<ModelEndpoint>> ScanAndSortAsync(CancellationToken ct)
    {
        var models = await ScanDirectoryAsync<ModelEndpoint>(_dir, null, ct);
        return models
            .OrderByDescending(m => m.IsDefault)
            .ThenBy(m => m.CreatedAt)
            .ToList();
    }
}
