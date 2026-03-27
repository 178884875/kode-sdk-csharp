using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Events;
using Kode.Agent.Sdk.Core.Skills;
using Kode.Agent.Sdk.Core.Todo;
using Kode.Agent.Sdk.Core.Types;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Kode.Agent.Store.Redis;

/// <summary>
/// Redis-based implementation of IAgentStore for distributed persistence.
/// </summary>
public class RedisAgentStore : IAgentStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisAgentStore>? _logger;
    private readonly RedisStoreOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    private IDatabase Database => _redis.GetDatabase(_options.Database);

    // StackExchange.Redis 2.12+ replaced TimeSpan? with the Expiration struct.
    // TimeSpan has an implicit conversion to Expiration; default(Expiration) means no expiry.
    private Expiration DataExpiry => _options.Expiration is { } ts ? (Expiration)ts : default;

    public RedisAgentStore(
        IConnectionMultiplexer redis,
        RedisStoreOptions? options = null,
        ILogger<RedisAgentStore>? logger = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? new RedisStoreOptions();
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        _jsonOptions.Converters.Add(new AgentEventJsonConverter());
    }

    #region Key Generation
    
    private string Key(string agentId, string suffix) => $"{_options.KeyPrefix}:{agentId}:{suffix}";
    private string IndexKey() => $"{_options.KeyPrefix}:agents";
    
    #endregion

    #region Runtime State

    public async Task SaveMessagesAsync(string agentId, IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(messages, _jsonOptions);
        await db.StringSetAsync(Key(agentId, "messages"), json, DataExpiry);
        await db.SetAddAsync(IndexKey(), agentId);
    }

    public async Task<IReadOnlyList<Message>> LoadMessagesAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = await db.StringGetAsync(Key(agentId, "messages"));
        if (json.IsNullOrEmpty) return [];
        return JsonSerializer.Deserialize<List<Message>>((string)json!, _jsonOptions) ?? [];
    }

    public async Task SaveToolCallRecordsAsync(string agentId, IReadOnlyList<ToolCallRecord> records, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(records, _jsonOptions);
        await db.StringSetAsync(Key(agentId, "tool-calls"), json, DataExpiry);
    }

    public async Task<IReadOnlyList<ToolCallRecord>> LoadToolCallRecordsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = await db.StringGetAsync(Key(agentId, "tool-calls"));
        if (json.IsNullOrEmpty) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ToolCallRecord>>((string)json!, _jsonOptions) ?? [];
        }
        catch
        {
            // Back-compat: older tool-calls payload shape.
            try
            {
                var legacy = JsonSerializer.Deserialize<List<LegacyToolCallRecord>>((string)json!, _jsonOptions) ?? [];
                return legacy.Select(ConvertLegacy).ToList();
            }
            catch
            {
                return [];
            }
        }
    }

    private sealed record LegacyToolCallRecord
    {
        public string? CallId { get; init; }
        public string? ToolName { get; init; }
        public object? Arguments { get; init; }
        public int State { get; init; }
        public object? Result { get; init; }
        public string? Error { get; init; }
        public long? StartedAt { get; init; }
        public long? CompletedAt { get; init; }
    }

    private static ToolCallRecord ConvertLegacy(LegacyToolCallRecord legacy)
    {
        var id = legacy.CallId ?? Guid.NewGuid().ToString("N");
        var name = legacy.ToolName ?? "tool";
        var input = legacy.Arguments ?? new { };
        var state = (ToolCallState)legacy.State;

        var createdAt = legacy.StartedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var updatedAt = legacy.CompletedAt ?? legacy.StartedAt ?? createdAt;
        var durationMs = legacy.StartedAt != null && legacy.CompletedAt != null
            ? Math.Max(0, legacy.CompletedAt.Value - legacy.StartedAt.Value)
            : (long?)null;

        return new ToolCallRecord
        {
            Id = id,
            Name = name,
            Input = input,
            State = state,
            Approval = new ToolCallApproval { Required = state == ToolCallState.ApprovalRequired },
            Result = legacy.Result,
            Error = legacy.Error,
            IsError = state is ToolCallState.Failed or ToolCallState.Denied or ToolCallState.Sealed,
            StartedAt = legacy.StartedAt,
            CompletedAt = legacy.CompletedAt,
            DurationMs = durationMs,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            AuditTrail =
            [
                new ToolCallAuditEntry { State = state, Timestamp = updatedAt, Note = "migrated" }
            ]
        };
    }

    public async Task SaveTodosAsync(string agentId, TodoSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await db.StringSetAsync(Key(agentId, "todos"), json, DataExpiry);
    }

    public async Task<TodoSnapshot?> LoadTodosAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = await db.StringGetAsync(Key(agentId, "todos"));
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<TodoSnapshot>((string)json!, _jsonOptions);
    }

    #endregion

    #region Events

    public async Task AppendEventAsync(string agentId, Timeline timeline, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(timeline, _jsonOptions);
        var channel = (timeline.Event.Channel ?? "monitor").ToLowerInvariant();
        if (channel is not ("progress" or "control" or "monitor"))
        {
            channel = "monitor";
        }
        await db.ListRightPushAsync(Key(agentId, $"events:{channel}"), json);
    }

    public async IAsyncEnumerable<Timeline> ReadEventsAsync(
        string agentId,
        EventChannel? channel = null,
        Bookmark? since = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var db = Database;
        var channels = channel.HasValue
            ? [channel.Value.ToString().ToLowerInvariant()]
            : new[] { "progress", "control", "monitor" };

        foreach (var ch in channels)
        {
            var values = await db.ListRangeAsync(Key(agentId, $"events:{ch}"));
            foreach (var value in values)
            {
                if (value.IsNullOrEmpty) continue;
                Timeline? timeline = null;
                try
                {
                    timeline = JsonSerializer.Deserialize<Timeline>((string)value!, _jsonOptions);
                }
                catch
                {
                    try
                    {
                        var legacy = JsonSerializer.Deserialize<LegacyTimeline>((string)value!, _jsonOptions);
                        if (legacy != null)
                        {
                            timeline = ConvertLegacy(legacy);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (timeline == null) continue;
                if (since != null && timeline.Bookmark.Seq <= since.Seq) continue;
                yield return timeline;
            }
        }
    }

    private sealed record LegacyTimeline
    {
        public long Seq { get; init; }
        public long Timestamp { get; init; }
        public EventChannel Channel { get; init; }
        public required AgentEvent Event { get; init; }
    }

    private static string ToAgentChannel(EventChannel channel) =>
        channel switch
        {
            EventChannel.Progress => "progress",
            EventChannel.Control => "control",
            EventChannel.Monitor => "monitor",
            _ => "monitor"
        };

    private static Timeline ConvertLegacy(LegacyTimeline legacy)
    {
        var bookmark = legacy.Event.Bookmark ?? new Bookmark { Seq = legacy.Seq, Timestamp = legacy.Timestamp };
        var ev = legacy.Event;
        if (ev.Channel == null || string.IsNullOrWhiteSpace(ev.Channel))
        {
            ev = ev with { Channel = ToAgentChannel(legacy.Channel) };
        }
        if (ev.Bookmark == null)
        {
            ev = ev with { Bookmark = bookmark };
        }

        return new Timeline
        {
            Cursor = legacy.Seq,
            Bookmark = bookmark,
            Event = ev
        };
    }

    #endregion

    #region History / Compression

    public async Task SaveHistoryWindowAsync(string agentId, HistoryWindow window, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(window, _jsonOptions);
        await db.StringSetAsync(Key(agentId, $"history:windows:{window.Id}"), json, DataExpiry);
        await db.SetAddAsync(Key(agentId, "history:windows:index"), window.Id);
    }

    public async Task<IReadOnlyList<HistoryWindow>> LoadHistoryWindowsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var ids = await db.SetMembersAsync(Key(agentId, "history:windows:index"));
        if (ids.Length == 0) return [];

        var result = new List<HistoryWindow>();
        foreach (var id in ids.Select(v => (string)v!).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var json = await db.StringGetAsync(Key(agentId, $"history:windows:{id}"));
            if (json.IsNullOrEmpty) continue;
            var item = JsonSerializer.Deserialize<HistoryWindow>((string)json!, _jsonOptions);
            if (item != null) result.Add(item);
        }
        return result.OrderBy(w => w.Timestamp).ToList();
    }

    public async Task SaveCompressionRecordAsync(string agentId, CompressionRecord record, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(record, _jsonOptions);
        await db.StringSetAsync(Key(agentId, $"history:compressions:{record.Id}"), json, DataExpiry);
        await db.SetAddAsync(Key(agentId, "history:compressions:index"), record.Id);
    }

    public async Task<IReadOnlyList<CompressionRecord>> LoadCompressionRecordsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var ids = await db.SetMembersAsync(Key(agentId, "history:compressions:index"));
        if (ids.Length == 0) return [];

        var result = new List<CompressionRecord>();
        foreach (var id in ids.Select(v => (string)v!).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var json = await db.StringGetAsync(Key(agentId, $"history:compressions:{id}"));
            if (json.IsNullOrEmpty) continue;
            var item = JsonSerializer.Deserialize<CompressionRecord>((string)json!, _jsonOptions);
            if (item != null) result.Add(item);
        }
        return result.OrderBy(r => r.Timestamp).ToList();
    }

    public async Task SaveRecoveredFileAsync(string agentId, RecoveredFile file, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var id = $"{file.Timestamp}:{file.Path}";
        var json = JsonSerializer.Serialize(file, _jsonOptions);
        await db.StringSetAsync(Key(agentId, $"history:recovered:{id}"), json, DataExpiry);
        await db.SetAddAsync(Key(agentId, "history:recovered:index"), id);
    }

    public async Task<IReadOnlyList<RecoveredFile>> LoadRecoveredFilesAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var ids = await db.SetMembersAsync(Key(agentId, "history:recovered:index"));
        if (ids.Length == 0) return [];

        var result = new List<RecoveredFile>();
        foreach (var id in ids.Select(v => (string)v!).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var json = await db.StringGetAsync(Key(agentId, $"history:recovered:{id}"));
            if (json.IsNullOrEmpty) continue;
            var item = JsonSerializer.Deserialize<RecoveredFile>((string)json!, _jsonOptions);
            if (item != null) result.Add(item);
        }
        return result.OrderBy(r => r.Timestamp).ToList();
    }

    #endregion

    #region Snapshots

    public async Task SaveSnapshotAsync(string agentId, Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await db.HashSetAsync(Key(agentId, "snapshots"), snapshot.Id, json);
    }

    public async Task<Snapshot?> LoadSnapshotAsync(string agentId, string snapshotId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = await db.HashGetAsync(Key(agentId, "snapshots"), snapshotId);
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<Snapshot>((string)json!, _jsonOptions);
    }

    public async Task<IReadOnlyList<Snapshot>> ListSnapshotsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var entries = await db.HashGetAllAsync(Key(agentId, "snapshots"));

        var result = new List<Snapshot>();
        foreach (var entry in entries)
        {
            if (entry.Value.IsNullOrEmpty) continue;
            var snapshot = JsonSerializer.Deserialize<Snapshot>((string)entry.Value!, _jsonOptions);
            if (snapshot != null) result.Add(snapshot);
        }

        return result.OrderBy(s => s.CreatedAt, StringComparer.Ordinal).ToList();
    }

    public async Task DeleteSnapshotAsync(string agentId, string snapshotId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        await db.HashDeleteAsync(Key(agentId, "snapshots"), snapshotId);
    }

    #endregion

    #region Meta

    public async Task SaveInfoAsync(string agentId, AgentInfo info, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(info, _jsonOptions);
        await db.StringSetAsync(Key(agentId, "meta"), json, DataExpiry);
        await db.SetAddAsync(IndexKey(), agentId);
    }

    public async Task<AgentInfo?> LoadInfoAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = await db.StringGetAsync(Key(agentId, "meta"));
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<AgentInfo>((string)json!, _jsonOptions);
    }

    #endregion

    #region Skills State

    public async Task SaveSkillsStateAsync(string agentId, SkillsState state, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        await db.StringSetAsync(Key(agentId, "skills"), json, DataExpiry);
    }

    public async Task<SkillsState?> LoadSkillsStateAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var json = await db.StringGetAsync(Key(agentId, "skills"));
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<SkillsState>((string)json!, _jsonOptions);
    }

    #endregion

    #region Agent Lifecycle

    public async Task<bool> ExistsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        return await db.SetContainsAsync(IndexKey(), agentId);
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var db = Database;
        var members = await db.SetMembersAsync(IndexKey());
        return members.Select(m => m.ToString()).ToList();
    }

    public async Task DeleteAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var db = Database;
        var batch = db.CreateBatch();

        // Delete all keys for this agent
        var keysToDelete = new[]
        {
            Key(agentId, "messages"),
            Key(agentId, "tool-calls"),
            Key(agentId, "todos"),
            Key(agentId, "skills"),
            Key(agentId, "snapshots"),
            Key(agentId, "events:progress"),
            Key(agentId, "events:control"),
            Key(agentId, "events:monitor")
        };

        foreach (var key in keysToDelete)
        {
            _ = batch.KeyDeleteAsync(key);
        }

        _ = batch.SetRemoveAsync(IndexKey(), agentId);
        
        batch.Execute();
        await Task.CompletedTask;
    }

    #endregion
}

/// <summary>
/// Options for configuring RedisAgentStore.
/// </summary>
public class RedisStoreOptions
{
    /// <summary>
    /// Key prefix for all Redis keys.
    /// </summary>
    public string KeyPrefix { get; set; } = "kode:agent";

    /// <summary>
    /// Redis database number.
    /// </summary>
    public int Database { get; set; } = 0;

    /// <summary>
    /// Optional expiration for agent data.
    /// </summary>
    public TimeSpan? Expiration { get; set; }

    /// <summary>
    /// Connection string for Redis.
    /// </summary>
    public string? ConnectionString { get; set; }
}
