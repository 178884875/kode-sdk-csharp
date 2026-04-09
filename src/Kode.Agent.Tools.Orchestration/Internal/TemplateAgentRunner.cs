using System.Diagnostics;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Agent;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Skills;
using Kode.Agent.Sdk.Core.Types;
using AgentRuntime = Kode.Agent.Sdk.Core.Agent.Agent;

namespace Kode.Agent.Tools.Orchestration.Internal;

/// <summary>
/// Request parameters for running a template-based sub-agent.
/// </summary>
internal sealed record TemplateRunRequest
{
    public required LoadedTemplate Template { get; init; }
    public required string Prompt { get; init; }
    public string? WorkDir { get; init; }
    public int? MaxIterationsOverride { get; init; }
    public int? MaxContextTokensOverride { get; init; }
    public MaxIterationsMode MaxIterationsMode { get; init; } = MaxIterationsMode.Fixed;
    public required IModelProvider ModelProvider { get; init; }
    public required string ModelId { get; init; }
    public required IToolRegistry ToolRegistry { get; init; }
    public required ISandboxFactory SandboxFactory { get; init; }
    public Microsoft.Extensions.Logging.ILoggerFactory? LoggerFactory { get; init; }
    public SandboxOptions? ParentSandboxOptions { get; init; }
    public string? ParentAgentId { get; init; }
    public string? ToolCallId { get; init; }
    /// <summary>
    /// Optional callback to forward events to the parent agent's Monitor channel.
    /// Receives the parent agent's IEventBus, allowing typed Monitor events.
    /// </summary>
    public IEventBus? ParentEventBus { get; init; }

    /// <summary>
    /// Skills search paths to use when auto-activating skills from the template.
    /// If null, skills auto-activation is skipped even when the template declares it.
    /// </summary>
    public IReadOnlyList<string>? SkillsPaths { get; init; }
}

/// <summary>
/// Result from a template-based sub-agent run.
/// </summary>
internal sealed record TemplateRunResult
{
    public bool Success { get; init; }
    public string? Summary { get; init; }
    public string? Error { get; init; }
    public string? StopReason { get; init; }
    public TokenUsage? TokenUsage { get; init; }
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];

    public static TemplateRunResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Core execution engine for template-based sub-agents.
/// Solves L1 (no AllowedTools filter), L3 (depth tracking), L4 (event visibility),
/// L7 (token reporting), L8 (AllowAll = true ToolRegistry.List()).
/// </summary>
internal static class TemplateAgentRunner
{
    public static async Task<TemplateRunResult> RunAsync(
        TemplateRunRequest request,
        CancellationToken cancellationToken)
    {
        var template = request.Template.Definition;
        var templateId = template.Id;

        // ── Step 1: Resolve tool list (L1 / L3 / L8) ────────────────────────
        IReadOnlyList<string> tools;
        if (template.Tools.AllowAll)
        {
            // L8: AllowAll → truly all registered tools
            tools = request.ToolRegistry.List();
        }
        else
        {
            var allowed = template.Tools.AllowedTools ?? [];
            var validated = allowed
                .Where(t => request.ToolRegistry.Has(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            tools = validated;
        }

        // L3: Remove spawn_agent from the tool list if current depth >= template's allowed depth
        int allowedDepth = template.Runtime?.SubAgents?.Depth ?? 0;
        if (SpawnDepthTracker.Current >= allowedDepth)
        {
            tools = tools
                .Where(t => !string.Equals(t, "spawn_agent", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Problem 3: Also remove DenyTools from the schema so the model never sees them
        var denySet = template.Permission?.DenyTools;
        if (denySet is { Count: > 0 })
        {
            tools = tools
                .Where(t => !denySet.Any(d => string.Equals(d, t, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (tools.Count == 0 && !template.Tools.AllowAll)
            return TemplateRunResult.Fail(
                $"No valid tools remain for template '{templateId}'. " +
                "Verify that the template's allowedTools are registered in the ToolRegistry.");

        // ── Step 2: Build AgentConfig ─────────────────────────────────────────
        var childWorkDir = request.WorkDir ?? request.ParentSandboxOptions?.WorkingDirectory;
        var childSandbox = new SandboxOptions
        {
            WorkingDirectory = childWorkDir,
            EnforceBoundary = request.ParentSandboxOptions?.EnforceBoundary ?? true,
            AllowPaths = SubAgentRunner.BuildAllowPaths(request.ParentSandboxOptions, request.WorkDir),
            WatchFiles = false,
        };

        PermissionConfig? permissions = null;
        if (template.Permission != null)
        {
            permissions = new PermissionConfig
            {
                Mode = template.Permission.Mode ?? "auto",
                RequireApprovalTools = template.Permission.RequireApprovalTools,
                DenyTools = template.Permission.DenyTools,
            };
        }
        permissions ??= new PermissionConfig { Mode = "auto", RequireApprovalTools = [] };

        // Determine iteration budget and context window:
        // Priority: Auto-estimate > caller override > template value > hardcoded default
        var maxIterations = request.MaxIterationsOverride ?? request.Template.MaxIterations;
        var maxContextTokens = request.MaxContextTokensOverride ?? request.Template.MaxContextTokens;

        if (request.MaxIterationsMode == MaxIterationsMode.Auto)
        {
            var estimate = await ComplexityEstimator.EstimateAsync(
                task: request.Prompt,
                workDir: childWorkDir,
                modelId: request.ModelId,
                modelProvider: request.ModelProvider,
                toolRegistry: request.ToolRegistry,
                sandboxFactory: request.SandboxFactory,
                fallbackIterations: maxIterations,
                fallbackContextTokens: maxContextTokens,
                loggerFactory: request.LoggerFactory,
                cancellationToken: cancellationToken);
            maxIterations = estimate.MaxIterations;
            maxContextTokens = estimate.MaxContextTokens;
        }

        // Problem 2: build SkillsConfig when skills paths are provided and template declares auto-activate
        var autoActivateSkills = request.Template.AutoActivateSkills;
        SkillsConfig? skillsConfig = null;
        if (request.SkillsPaths is { Count: > 0 } && autoActivateSkills is { Count: > 0 })
        {
            skillsConfig = new SkillsConfig
            {
                Paths = request.SkillsPaths,
                AutoActivate = autoActivateSkills,
            };
        }

        var config = new AgentConfig
        {
            Model = template.Model ?? request.ModelId,
            SystemPrompt = template.SystemPrompt,
            MaxIterations = Math.Clamp(maxIterations, 1, 100),
            Tools = tools.Count > 0 ? tools : null,
            SandboxOptions = childSandbox,
            Permissions = permissions,
            Skills = skillsConfig,
            Context = new ContextManagerOptions
            {
                MaxTokens = Math.Max(20_000, maxContextTokens),
                CompressToTokens = (int)(Math.Max(20_000, maxContextTokens) * 0.625),
                ToolResultCompression = new ToolResultCompressionOptions { Enabled = true },
            },
            AgentRole = "sub-agent",
            ParentActivityContext = Activity.Current?.Context ?? default,
        };

        // ── Step 3: Build AgentDependencies ───────────────────────────────────
        // Problem 5: ensure the Guid suffix is always present for uniqueness
        var guidSuffix = Guid.NewGuid().ToString("N")[..8];
        var templatePart = templateId.Length > 20 ? templateId[..20] : templateId;
        var subAgentId = $"tpl-{templatePart}-{guidSuffix}";

        var deps = new AgentDependencies
        {
            Store = new InMemoryAgentStore(),
            SandboxFactory = request.SandboxFactory,
            ToolRegistry = request.ToolRegistry,
            ModelProvider = request.ModelProvider,
            LoggerFactory = request.LoggerFactory,
        };

        // ── Step 4: Create sub-agent ──────────────────────────────────────────
        await using var subAgent = await AgentRuntime.CreateAsync(subAgentId, config, deps, cancellationToken);

        // ── Step 5: Notify parent (L4) ────────────────────────────────────────
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        request.ParentEventBus?.EmitMonitor(new SubAgentCreatedEvent
        {
            Type = "subagent.created",
            AgentId = subAgentId,
            TemplateId = templateId,
            ParentAgentId = request.ParentAgentId ?? "unknown",
            CallId = request.ToolCallId,
            Timestamp = now,
        });

        // ── Step 6: Forward events to parent Monitor channel (L4) ─────────────
        using var forwardCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var eventForwardTask = ForwardEventsAsync(
            subAgent.EventBus,
            request.ParentEventBus,
            subAgentId,
            templateId,
            request.ToolCallId,
            forwardCts.Token);

        // ── Step 7: Run sub-agent (L3: depth tracking) ───────────────────────
        AgentRunResult runResult;
        using (SpawnDepthTracker.Enter())
        {
            runResult = await subAgent.RunAsync(request.Prompt, cancellationToken);
        }

        // ── Step 8: Stop event forwarding ─────────────────────────────────────
        await forwardCts.CancelAsync();
        try { await eventForwardTask; } catch (OperationCanceledException) { }

        if (!runResult.Success)
            return TemplateRunResult.Fail(
                $"Sub-agent '{templateId}' failed (stopReason={runResult.StopReason}): {runResult.Response}");

        return new TemplateRunResult
        {
            Success = true,
            Summary = runResult.Response ?? "(no output)",
            StopReason = runResult.StopReason.ToString(),
            TokenUsage = runResult.TokenUsage,    // L7
            ToolsUsed = tools,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task ForwardEventsAsync(
        IEventBus subBus,
        IEventBus? parentBus,
        string subAgentId,
        string templateId,
        string? parentCallId,
        CancellationToken ct)
    {
        if (parentBus == null) return;

        try
        {
            await foreach (var envelope in subBus.SubscribeAsync(EventChannel.Progress, null, ct))
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                switch (envelope.Event)
                {
                    case TextChunkEvent chunk:
                        parentBus.EmitMonitor(new SubAgentDeltaEvent
                        {
                            Type = "subagent.delta",
                            SubAgentId = subAgentId,
                            TemplateId = templateId,
                            CallId = parentCallId,
                            Delta = chunk.Delta,
                            Text = chunk.Delta,
                            Step = chunk.Step,
                            Timestamp = ts,
                        });
                        break;

                    case ThinkChunkEvent think:
                        parentBus.EmitMonitor(new SubAgentThinkingEvent
                        {
                            Type = "subagent.thinking",
                            SubAgentId = subAgentId,
                            TemplateId = templateId,
                            CallId = parentCallId,
                            Delta = think.Delta,
                            Step = think.Step,
                            Timestamp = ts,
                        });
                        break;

                    case ToolStartEvent toolStart:
                        parentBus.EmitMonitor(new SubAgentToolStartEvent
                        {
                            Type = "subagent.tool_start",
                            SubAgentId = subAgentId,
                            TemplateId = templateId,
                            ParentCallId = parentCallId,
                            ToolCallId = toolStart.Call.Id,
                            ToolName = toolStart.Call.Name,
                            Timestamp = ts,
                        });
                        break;

                    case ToolEndEvent toolEnd:
                        parentBus.EmitMonitor(new SubAgentToolEndEvent
                        {
                            Type = "subagent.tool_end",
                            SubAgentId = subAgentId,
                            TemplateId = templateId,
                            ParentCallId = parentCallId,
                            ToolCallId = toolEnd.Call.Id,
                            ToolName = toolEnd.Call.Name,
                            Timestamp = ts,
                        });
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
