using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Skills;
using Kode.Agent.Sdk.Core.Todo;
using Kode.Agent.Sdk.Core.Types;

namespace KodaClaw.ChannelHub.Commands;

/// <summary>
/// A no-op <see cref="IAgentStore"/> for ephemeral (one-shot) agents that do not
/// need state persistence. Used by the /btw side-question handler.
/// Main session history is injected via the system prompt (plain-text block), not via
/// this store, so all store operations are intentional no-ops.
/// </summary>
internal sealed class EphemeralAgentStore : IAgentStore
{
    public Task<bool> ExistsAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<AgentInfo?> LoadInfoAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<AgentInfo?>(null);

    public Task SaveMessagesAsync(string agentId, IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<Message>> LoadMessagesAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Message>>([]);

    public Task SaveToolCallRecordsAsync(string agentId, IReadOnlyList<ToolCallRecord> records, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ToolCallRecord>> LoadToolCallRecordsAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ToolCallRecord>>([]);

    public Task SaveTodosAsync(string agentId, TodoSnapshot snapshot, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<TodoSnapshot?> LoadTodosAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<TodoSnapshot?>(null);

    public Task AppendEventAsync(string agentId, Timeline timeline, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

#pragma warning disable CS1998 // async iterator without await
    public async IAsyncEnumerable<Timeline> ReadEventsAsync(
        string agentId,
        EventChannel? channel = null,
        Bookmark? since = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield break;
    }
#pragma warning restore CS1998

    public Task SaveHistoryWindowAsync(string agentId, HistoryWindow window, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<HistoryWindow>> LoadHistoryWindowsAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<HistoryWindow>>([]);

    public Task SaveCompressionRecordAsync(string agentId, CompressionRecord record, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<CompressionRecord>> LoadCompressionRecordsAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CompressionRecord>>([]);

    public Task SaveRecoveredFileAsync(string agentId, RecoveredFile file, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<RecoveredFile>> LoadRecoveredFilesAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RecoveredFile>>([]);

    public Task SaveSnapshotAsync(string agentId, Snapshot snapshot, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<Snapshot?> LoadSnapshotAsync(string agentId, string snapshotId, CancellationToken cancellationToken = default)
        => Task.FromResult<Snapshot?>(null);

    public Task<IReadOnlyList<Snapshot>> ListSnapshotsAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Snapshot>>([]);

    public Task DeleteSnapshotAsync(string agentId, string snapshotId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SaveInfoAsync(string agentId, AgentInfo info, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SaveSkillsStateAsync(string agentId, SkillsState state, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<SkillsState?> LoadSkillsStateAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult<SkillsState?>(null);

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task DeleteAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// A no-op <see cref="IToolRegistry"/> for ephemeral agents that need no tools.
/// Used by the /btw side-question handler.
/// </summary>
internal sealed class EphemeralToolRegistry : IToolRegistry
{
    public void Register(string id, Func<ToolFactoryContext, ITool> factory) { }
    public void Register(ITool tool) { }

    public ITool Create(string id, Dictionary<string, object>? config = null)
        => throw new NotSupportedException("EphemeralToolRegistry: no tools registered.");

    public bool Has(string id) => false;

    public IReadOnlyList<string> List() => [];

    public ITool? Get(string name) => null;
}
