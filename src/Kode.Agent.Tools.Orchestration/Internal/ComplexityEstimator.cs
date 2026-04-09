using System.Diagnostics;
using System.Text.Json;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Agent;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Types;
using AgentRuntime = Kode.Agent.Sdk.Core.Agent.Agent;

namespace Kode.Agent.Tools.Orchestration.Internal;

/// <summary>
/// Result of a complexity estimation run.
/// </summary>
internal record ComplexityEstimate(int MaxIterations, int MaxContextTokens);

/// <summary>
/// Lightweight task-complexity estimator shared by <see cref="SubAgentRunner"/>
/// and <see cref="TemplateAgentRunner"/>.
///
/// When <c>MaxIterationsMode.Auto</c> is requested, this class spawns a no-tool
/// sub-agent that analyses the task description and returns recommended values for
/// <c>MaxIterations</c> and <c>MaxContextTokens</c>.  The call is best-effort:
/// any failure silently falls back to the caller-supplied defaults.
/// </summary>
internal static class ComplexityEstimator
{
    private const string SystemPrompt =
        "You are a task complexity estimator for an AI agent that uses tools " +
        "(file reads, code search, bash commands). " +
        "Analyse the task description and estimate the resources needed.\n\n" +
        "Reply with ONLY valid JSON (no markdown fences, no explanation):\n" +
        "{\n" +
        "  \"iterations\": <integer 5–50>,\n" +
        "  \"contextTokens\": <one of: 40000 | 80000 | 120000 | 160000>,\n" +
        "  \"reason\": \"<one sentence>\"\n" +
        "}\n\n" +
        "Iteration guidelines (each file read / grep / bash ≈ 1–2 iterations):\n" +
        "  5–8   : simple lookup, 1–5 tool calls\n" +
        "  10–15 : moderate investigation, 5–15 tool calls\n" +
        "  18–25 : deep analysis, 15–30 tool calls\n" +
        "  30–50 : comprehensive audit, 30+ tool calls\n\n" +
        "Context token guidelines:\n" +
        "  40000  : no files, or a handful of small ones\n" +
        "  80000  : several files or one large file\n" +
        "  120000 : many files or a full module/component\n" +
        "  160000 : large codebase sections or many large files";

    /// <summary>
    /// Estimates task complexity by running a no-tool micro-agent.
    /// Returns <paramref name="fallback"/> on any failure.
    /// </summary>
    internal static async Task<ComplexityEstimate> EstimateAsync(
        string task,
        string? workDir,
        string modelId,
        IModelProvider modelProvider,
        IToolRegistry toolRegistry,
        ISandboxFactory sandboxFactory,
        int fallbackIterations,
        int fallbackContextTokens,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        var fallback = new ComplexityEstimate(
            Math.Clamp(fallbackIterations, 1, 50),
            Math.Max(20_000, fallbackContextTokens));

        try
        {
            var config = new AgentConfig
            {
                Model = modelId,
                SystemPrompt = SystemPrompt,
                MaxIterations = 2,
                Tools = null,   // pure reasoning — no tools needed
                SandboxOptions = new SandboxOptions
                {
                    WorkingDirectory = workDir,
                    EnforceBoundary = false,
                    WatchFiles = false,
                },
                Permissions = new PermissionConfig { Mode = "auto", RequireApprovalTools = [] },
                Context = new ContextManagerOptions
                {
                    MaxTokens = 20_000,
                    CompressToTokens = 12_500,
                    ToolResultCompression = new ToolResultCompressionOptions { Enabled = false },
                },
                AgentRole = "sub-agent",
                ParentActivityContext = Activity.Current?.Context ?? default,
            };

            var deps = new AgentDependencies
            {
                Store = new InMemoryAgentStore(),
                SandboxFactory = sandboxFactory,
                ToolRegistry = toolRegistry,
                ModelProvider = modelProvider,
                LoggerFactory = loggerFactory,
            };

            var agentId = $"est-{Guid.NewGuid():N}"[..24];
            await using var agent = await AgentRuntime.CreateAsync(agentId, config, deps, cancellationToken);
            var result = await agent.RunAsync(task, cancellationToken);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Response))
                return fallback;

            return ParseResponse(result.Response, fallback);
        }
        catch
        {
            // Estimation is best-effort; never block the real task.
            return fallback;
        }
    }

    private static ComplexityEstimate ParseResponse(string response, ComplexityEstimate fallback)
    {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start) return fallback;

        try
        {
            using var doc = JsonDocument.Parse(response[start..(end + 1)]);
            var root = doc.RootElement;

            var iterations = root.TryGetProperty("iterations", out var iterProp)
                             && iterProp.TryGetInt32(out var iter)
                ? Math.Clamp(iter, 5, 50)
                : fallback.MaxIterations;

            var contextTokens = root.TryGetProperty("contextTokens", out var ctxProp)
                                && ctxProp.TryGetInt32(out var ctx)
                ? Math.Max(20_000, ctx)
                : fallback.MaxContextTokens;

            return new ComplexityEstimate(iterations, contextTokens);
        }
        catch
        {
            return fallback;
        }
    }
}
