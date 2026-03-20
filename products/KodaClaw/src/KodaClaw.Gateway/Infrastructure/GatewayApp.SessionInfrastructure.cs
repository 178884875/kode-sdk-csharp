using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Store.Json;
using System.IO;

public static partial class GatewayApp
{
    private static async Task<IReadOnlyList<SessionDetail>> LoadSessionsAsync(
        string workspaceRoot,
        string? activeMainSessionId,
        int limit,
        CancellationToken cancellationToken)
    {
        var store = CreateSessionStore(workspaceRoot);
        var sessionIds = await store.ListAsync(cancellationToken);
        if (sessionIds.Count == 0)
        {
            return [];
        }

        var details = new List<SessionDetail>(sessionIds.Count);
        foreach (var sessionId in sessionIds)
        {
            var detail = await LoadSessionDetailAsync(store, sessionId, activeMainSessionId, cancellationToken);
            if (detail is not null)
            {
                details.Add(detail);
            }
        }

        return details
            .OrderByDescending(static detail => detail.LastEventAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(static detail => detail.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(static detail => detail.SessionId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static Task<SessionDetail?> LoadSessionDetailAsync(
        string workspaceRoot,
        string sessionId,
        string? activeMainSessionId,
        CancellationToken cancellationToken)
    {
        var store = CreateSessionStore(workspaceRoot);
        return LoadSessionDetailAsync(store, sessionId, activeMainSessionId, cancellationToken);
    }

    private static async Task<SessionDetail?> LoadSessionDetailAsync(
        IAgentStore store,
        string sessionId,
        string? activeMainSessionId,
        CancellationToken cancellationToken)
    {
        var info = await store.LoadInfoAsync(sessionId, cancellationToken);
        if (info is null)
        {
            return null;
        }

        var messages = await store.LoadMessagesAsync(sessionId, cancellationToken);
        var toolCalls = await store.LoadToolCallRecordsAsync(sessionId, cancellationToken);

        var pendingApprovalCallIds = toolCalls
            .Where(static call =>
                call.State == ToolCallState.ApprovalRequired &&
                call.Approval.Required &&
                string.IsNullOrWhiteSpace(call.Approval.Decision))
            .Select(static call => call.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var messageCount = messages.Count > 0 ? messages.Count : info.MessageCount;
        var userMessageCount = messages.Count(static message => message.Role == MessageRole.User);
        var assistantMessageCount = messages.Count(static message => message.Role == MessageRole.Assistant);

        return new SessionDetail(
            SessionId: sessionId,
            SessionKind: ResolveSessionKind(sessionId),
            Status: new SessionStatusSummary(
                IsActiveMainSession: string.Equals(activeMainSessionId, sessionId, StringComparison.Ordinal),
                BreakpointState: info.Breakpoint?.ToString(),
                MessageCount: messageCount,
                PendingApprovalCount: pendingApprovalCallIds.Length),
            CreatedAt: ParseDateTimeOffset(info.CreatedAt),
            LastEventAt: ParseBookmarkTimestamp(info.LastBookmark?.Timestamp),
            UserMessageCount: userMessageCount,
            AssistantMessageCount: assistantMessageCount,
            ToolCallCount: toolCalls.Count,
            LastSfpIndex: info.LastSfpIndex,
            PendingApprovalCallIds: pendingApprovalCallIds);
    }

    private static SessionKind ResolveSessionKind(string sessionId)
    {
        if (sessionId.StartsWith("channel-dm-", StringComparison.OrdinalIgnoreCase))
        {
            return SessionKind.ChannelDirectMessage;
        }

        if (sessionId.StartsWith("channel-group-", StringComparison.OrdinalIgnoreCase))
        {
            return SessionKind.ChannelGroup;
        }

        if (sessionId.StartsWith("auto-", StringComparison.OrdinalIgnoreCase))
        {
            return SessionKind.Automation;
        }

        if (sessionId.StartsWith("plugin-", StringComparison.OrdinalIgnoreCase))
        {
            return SessionKind.Plugin;
        }

        return SessionKind.Main;
    }

    private static IAgentStore CreateSessionStore(string workspaceRoot)
    {
        var sessionsRoot = Path.Combine(workspaceRoot, KodaClawWorkspaceLayout.SessionsDirectory);
        return new JsonAgentStore(sessionsRoot);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return DateTimeOffset.TryParse(rawValue, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? ParseBookmarkTimestamp(long? rawTimestamp)
    {
        if (rawTimestamp is null || rawTimestamp.Value <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(rawTimestamp.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
