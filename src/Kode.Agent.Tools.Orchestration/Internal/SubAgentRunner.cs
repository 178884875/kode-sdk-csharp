using System.Diagnostics;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Agent;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using AgentRuntime = Kode.Agent.Sdk.Core.Agent.Agent;

namespace Kode.Agent.Tools.Orchestration.Internal;

/// <summary>
/// Shared sub-agent spawn logic used by all orchestration tools.
/// Encapsulates tool whitelisting, sandbox inheritance, agent configuration, and result extraction.
/// </summary>
internal static class SubAgentRunner
{
    /// <summary>
    /// Tools that are safe to delegate by default — read-only, no side effects.
    /// </summary>
    internal static readonly IReadOnlyList<string> DefaultTools =
    [
        "fs_read", "fs_glob", "fs_grep", "fs_list",
        "bash_run", "bash_logs",
    ];

    /// <summary>
    /// Hard whitelist: write/channel tools are always stripped.
    /// Sub-agents must not produce side effects in the parent's world.
    /// </summary>
    internal static readonly HashSet<string> AllowedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "fs_read", "fs_glob", "fs_grep", "fs_list",
        "bash_run", "bash_logs", "bash_kill",
        "todo_read",
    };

    /// <summary>
    /// Spawns an isolated sub-agent, runs the given task, and returns a summary.
    /// </summary>
    internal static async Task<SubAgentResult> RunAsync(
        SubAgentRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Filter tools against the hard whitelist
        IReadOnlyList<string> tools;
        if (request.AllowNoTools && request.Tools is { Count: 0 })
        {
            // Explicit empty-tool request (e.g. context_distill, debate judge)
            tools = [];
        }
        else
        {
            var requestedTools = request.Tools ?? DefaultTools;
            var filtered = requestedTools
                .Where(t => AllowedTools.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (filtered.Count == 0)
                return SubAgentResult.Fail(
                    $"No allowed tools remain after filtering. Allowed: {string.Join(", ", AllowedTools)}");

            tools = filtered;
        }

        // 2. Build child sandbox — inherit parent's access rights
        var parentOpts = request.ParentSandboxOptions;
        var childWorkDir = request.WorkDir ?? parentOpts?.WorkingDirectory;
        var childSandboxOptions = new SandboxOptions
        {
            WorkingDirectory = childWorkDir,
            EnforceBoundary = parentOpts?.EnforceBoundary ?? true,
            AllowPaths = BuildAllowPaths(parentOpts, request.WorkDir),
            WatchFiles = false,
        };

        // 3. Build agent config
        var agentId = $"sub-{Guid.NewGuid():N}"[..24];
        var systemPrompt = request.SystemPromptOverride
                           ?? BuildSystemPrompt(request.Task, childWorkDir);

        // OT-1B: capture the calling tool's Activity as parent context so the sub-agent
        // run span becomes a child of the spawning "agent.tool.execute" span in the trace.
        // OT-2A: set AgentRole = "sub-agent" to enable cost attribution in token metrics.
        var config = new AgentConfig
        {
            Model = request.ModelId,
            SystemPrompt = systemPrompt,
            MaxIterations = Math.Clamp(request.MaxIterations, 1, 50),
            Tools = tools.Count > 0 ? tools : null,
            SandboxOptions = childSandboxOptions,
            Permissions = new PermissionConfig
            {
                Mode = "auto",
                RequireApprovalTools = [],
            },
            Context = new ContextManagerOptions
            {
                MaxTokens = 80_000,
                CompressToTokens = 50_000,
                ToolResultCompression = new ToolResultCompressionOptions { Enabled = true },
            },
            AgentRole = "sub-agent",
            ParentActivityContext = Activity.Current?.Context ?? default,
        };

        var deps = new AgentDependencies
        {
            Store = new InMemoryAgentStore(),
            SandboxFactory = request.SandboxFactory,
            ToolRegistry = request.ToolRegistry,
            ModelProvider = request.ModelProvider,
            LoggerFactory = request.LoggerFactory,
        };

        // 4. Run sub-agent
        await using var agent = await AgentRuntime.CreateAsync(agentId, config, deps, cancellationToken);
        var result = await agent.RunAsync(request.Task, cancellationToken);

        if (!result.Success)
            return SubAgentResult.Fail(
                $"Sub-agent failed (stopReason={result.StopReason}): {result.Response}");

        return new SubAgentResult
        {
            Success = true,
            Summary = result.Response ?? "(no output)",
            StopReason = result.StopReason.ToString(),
            ToolsUsed = tools,
        };
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    internal static string BuildSystemPrompt(string task, string? workDir)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "You are a focused sub-agent. Your only job is to complete the given task " +
            "and produce a concise, factual summary of your findings or actions.");

        if (!string.IsNullOrWhiteSpace(workDir))
            sb.AppendLine($"Working directory: {workDir}");

        sb.AppendLine();
        sb.AppendLine("Guidelines:");
        sb.AppendLine("- Use available tools to gather information or perform the task. Be systematic.");
        sb.AppendLine("- Do NOT send messages, modify workspace files outside the task scope, or create approvals.");
        sb.AppendLine("- When you have completed the task, write your final answer.");
        sb.AppendLine("- Keep the final answer under 500 words. Be specific and factual.");
        sb.AppendLine("- If something cannot be determined, say so explicitly.");
        sb.AppendLine();
        sb.AppendLine($"Task: {task}");

        return sb.ToString();
    }

    internal static IReadOnlyList<string>? BuildAllowPaths(SandboxOptions? parentOpts, string? extraWorkDir)
    {
        var paths = new List<string>();

        if (parentOpts?.AllowPaths is { Count: > 0 } existing)
            paths.AddRange(existing);

        if (!string.IsNullOrWhiteSpace(extraWorkDir)
            && !paths.Contains(extraWorkDir, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(extraWorkDir);
        }

        return paths.Count > 0 ? paths : null;
    }
}

/// <summary>
/// Parameters for a single sub-agent invocation.
/// </summary>
internal sealed class SubAgentRequest
{
    public required string Task { get; init; }
    public string? WorkDir { get; init; }
    public IReadOnlyList<string>? Tools { get; init; }
    public int MaxIterations { get; init; } = 20;
    public SandboxOptions? ParentSandboxOptions { get; init; }
    public required IModelProvider ModelProvider { get; init; }
    public required string ModelId { get; init; }
    public required IToolRegistry ToolRegistry { get; init; }
    public required ISandboxFactory SandboxFactory { get; init; }
    public Microsoft.Extensions.Logging.ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// When true, an explicitly empty <see cref="Tools"/> list is allowed —
    /// the sub-agent will respond using pure reasoning without calling any tools.
    /// Used by <c>context_distill</c>, <c>debate</c> judge/sides, etc.
    /// </summary>
    public bool AllowNoTools { get; init; } = false;

    /// <summary>
    /// If set, replaces the default system prompt entirely.
    /// Used by <c>ask_specialist</c> to inject a specialist role.
    /// </summary>
    public string? SystemPromptOverride { get; init; }
}

/// <summary>
/// Result from a sub-agent invocation.
/// </summary>
internal sealed class SubAgentResult
{
    public bool Success { get; init; }
    public string? Summary { get; init; }
    public string? Error { get; init; }
    public string? StopReason { get; init; }
    public IReadOnlyList<string>? ToolsUsed { get; init; }

    internal static SubAgentResult Fail(string error) => new() { Success = false, Error = error };
}
