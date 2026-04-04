using System.Runtime.CompilerServices;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Skills;
using Kode.Agent.Sdk.Core.Todo;
using Kode.Agent.Sdk.Core.Types;

namespace Kode.Agent.Tools.Orchestration.Internal;

/// <summary>
/// In-memory <see cref="IAgentStore"/> for ephemeral sub-agents whose data
/// is never persisted to disk. Used by <see cref="IsolateTaskTool"/> so that
/// temporary sub-agents leave no filesystem artifacts.
/// </summary>
internal sealed class InMemoryAgentStore : IAgentStore
{
    private readonly Dictionary<string, IReadOnlyList<Message>> _messages = new();
    private readonly Dictionary<string, IReadOnlyList<ToolCallRecord>> _toolCalls = new();
    private readonly Dictionary<string, TodoSnapshot> _todos = new();
    private readonly Dictionary<string, AgentInfo> _infos = new();
    private readonly Dictionary<string, SkillsState> _skills = new();

    // ── Runtime State ────────────────────────────────────────────────────────

    public Task SaveMessagesAsync(string agentId, IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
    {
        _messages[agentId] = messages;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Message>> LoadMessagesAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_messages.TryGetValue(agentId, out var m) ? m : (IReadOnlyList<Message>)[]);

    public Task SaveToolCallRecordsAsync(string agentId, IReadOnlyList<ToolCallRecord> records, CancellationToken cancellationToken = default)
    {
        _toolCalls[agentId] = records;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ToolCallRecord>> LoadToolCallRecordsAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_toolCalls.TryGetValue(agentId, out var r) ? r : (IReadOnlyList<ToolCallRecord>)[]);

    public Task SaveTodosAsync(string agentId, TodoSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _todos[agentId] = snapshot;
        return Task.CompletedTask;
    }

    public Task<TodoSnapshot?> LoadTodosAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_todos.TryGetValue(agentId, out var t) ? t : (TodoSnapshot?)null);

    // ── Events ───────────────────────────────────────────────────────────────
    // Sub-agent events are not observed by the parent; store is a no-op / empty.

    public Task AppendEventAsync(string agentId, Timeline timeline, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async IAsyncEnumerable<Timeline> ReadEventsAsync(
        string agentId,
        EventChannel? channel = null,
        Bookmark? since = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    // ── History / Compression ────────────────────────────────────────────────
    // Sub-agents are discarded after a single run; compression history not needed.

    public Task SaveHistoryWindowAsync(string agentId, HistoryWindow window, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<HistoryWindow>> LoadHistoryWindowsAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HistoryWindow>>([]);

    public Task SaveCompressionRecordAsync(string agentId, CompressionRecord record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<CompressionRecord>> LoadCompressionRecordsAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompressionRecord>>([]);

    public Task SaveRecoveredFileAsync(string agentId, RecoveredFile file, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<RecoveredFile>> LoadRecoveredFilesAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecoveredFile>>([]);

    // ── Snapshots ────────────────────────────────────────────────────────────

    public Task SaveSnapshotAsync(string agentId, Snapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<Snapshot?> LoadSnapshotAsync(string agentId, string snapshotId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Snapshot?>(null);

    public Task<IReadOnlyList<Snapshot>> ListSnapshotsAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Snapshot>>([]);

    public Task DeleteSnapshotAsync(string agentId, string snapshotId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    // ── Meta ─────────────────────────────────────────────────────────────────

    public Task SaveInfoAsync(string agentId, AgentInfo info, CancellationToken cancellationToken = default)
    {
        _infos[agentId] = info;
        return Task.CompletedTask;
    }

    public Task<AgentInfo?> LoadInfoAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_infos.TryGetValue(agentId, out var i) ? i : (AgentInfo?)null);

    // ── Skills State ─────────────────────────────────────────────────────────

    public Task SaveSkillsStateAsync(string agentId, SkillsState state, CancellationToken cancellationToken = default)
    {
        _skills[agentId] = state;
        return Task.CompletedTask;
    }

    public Task<SkillsState?> LoadSkillsStateAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_skills.TryGetValue(agentId, out var s) ? s : (SkillsState?)null);

    // ── Agent Lifecycle ───────────────────────────────────────────────────────

    public Task<bool> ExistsAsync(string agentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_messages.ContainsKey(agentId) || _infos.ContainsKey(agentId));

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(_messages.Keys.Union(_infos.Keys).Distinct().ToList());

    public Task DeleteAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _messages.Remove(agentId);
        _toolCalls.Remove(agentId);
        _todos.Remove(agentId);
        _infos.Remove(agentId);
        _skills.Remove(agentId);
        return Task.CompletedTask;
    }
}
