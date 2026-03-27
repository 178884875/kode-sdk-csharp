using System.Text.Json;
using Kode.Agent.Sdk.Core.Checkpoints;
using StackExchange.Redis;

namespace Kode.Agent.Store.Redis;

/// <summary>
/// Redis-based checkpointer (aligned with TS <c>RedisCheckpointer</c>).
/// </summary>
public sealed class RedisCheckpointer : ICheckpointer
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisCheckpointerOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    private IDatabase Database => _redis.GetDatabase(_options.Database);

    public RedisCheckpointer(IConnectionMultiplexer redis, RedisCheckpointerOptions? options = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? new RedisCheckpointerOptions();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    public async Task<string> SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = Database;
        var key = GetKey(checkpoint.Id);
        var json = JsonSerializer.Serialize(checkpoint, _jsonOptions);
        var expiry = _options.TtlSeconds is > 0 ? (Expiration)TimeSpan.FromSeconds(_options.TtlSeconds.Value) : default;

        await db.StringSetAsync(key, json, expiry);

        var indexKey = GetIndexKey(checkpoint.AgentId);
        await db.SortedSetAddAsync(indexKey, checkpoint.Id, checkpoint.Timestamp);

        return checkpoint.Id;
    }

    public async Task<Checkpoint?> LoadAsync(string checkpointId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = Database;
        var value = await db.StringGetAsync(GetKey(checkpointId));
        if (value.IsNullOrEmpty) return null;

        return JsonSerializer.Deserialize<Checkpoint>((string)value!, _jsonOptions);
    }

    public async Task<IReadOnlyList<CheckpointListItem>> ListAsync(
        string agentId,
        CheckpointListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = Database;
        var indexKey = GetIndexKey(agentId);

        var start = options?.Offset ?? 0;
        var stop = options?.Limit is > 0 ? start + options.Limit.Value - 1 : -1;

        var ids = await db.SortedSetRangeByRankAsync(indexKey, start, stop, Order.Descending);
        if (ids.Length == 0) return [];

        var items = new List<CheckpointListItem>();
        foreach (var idValue in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = (string?)idValue;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var checkpoint = await LoadAsync(id, cancellationToken);
            if (checkpoint == null) continue;

            if (!string.IsNullOrWhiteSpace(options?.SessionId) && checkpoint.SessionId != options.SessionId)
            {
                continue;
            }

            items.Add(new CheckpointListItem
            {
                Id = checkpoint.Id,
                AgentId = checkpoint.AgentId,
                SessionId = checkpoint.SessionId,
                Timestamp = checkpoint.Timestamp,
                IsForkPoint = checkpoint.Metadata.IsForkPoint,
                Tags = checkpoint.Metadata.Tags
            });
        }

        return items;
    }

    public async Task DeleteAsync(string checkpointId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var checkpoint = await LoadAsync(checkpointId, cancellationToken);
        if (checkpoint == null) return;

        var db = Database;
        await db.KeyDeleteAsync(GetKey(checkpointId));
        await db.SortedSetRemoveAsync(GetIndexKey(checkpoint.AgentId), checkpointId);
    }

    public async Task<string> ForkAsync(string checkpointId, string newAgentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var original = await LoadAsync(checkpointId, cancellationToken);
        if (original == null)
        {
            throw new KeyNotFoundException($"Checkpoint not found: {checkpointId}");
        }

        var forked = original with
        {
            Id = $"{newAgentId}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            AgentId = newAgentId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Metadata = original.Metadata with
            {
                ParentCheckpointId = checkpointId
            }
        };

        return await SaveAsync(forked, cancellationToken);
    }

    private string GetKey(string checkpointId) => $"{_options.KeyPrefix}{checkpointId}";
    private string GetIndexKey(string agentId) => $"{_options.KeyPrefix}index:{agentId}";
}

public sealed class RedisCheckpointerOptions
{
    public string KeyPrefix { get; set; } = "kode:checkpoint:";
    public int Database { get; set; } = 0;
    public int? TtlSeconds { get; set; }
}