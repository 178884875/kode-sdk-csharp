using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration.Internal;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Spawns a sub-agent based on a JSON template file.
/// The template defines the sub-agent's system prompt, allowed tools, model, and runtime configuration.
/// </summary>
[Tool("spawn_agent")]
[ToolAttributes(ReadOnly = false, NoEffect = false)]
public sealed class SpawnAgentTool : ToolBase<SpawnAgentArgs>
{
    private readonly IModelProvider _modelProvider;
    private readonly string _modelId;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;
    private readonly IReadOnlyList<string>? _skillsPaths;

    public SpawnAgentTool(
        IModelProvider modelProvider,
        string modelId,
        IToolRegistry toolRegistry,
        ISandboxFactory sandboxFactory,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null,
        IReadOnlyList<string>? skillsPaths = null)
    {
        _modelProvider = modelProvider;
        _modelId = modelId;
        _toolRegistry = toolRegistry;
        _sandboxFactory = sandboxFactory;
        _loggerFactory = loggerFactory;
        _skillsPaths = skillsPaths;
    }

    public override string Name => "spawn_agent";

    public override string Description =>
        "Spawns a specialized sub-agent from a JSON template file. " +
        "The template defines the sub-agent's role (system prompt), allowed tools, and runtime settings. " +
        "The sub-agent runs to completion and returns its result. " +
        "Use this for tasks that need a dedicated agent persona defined ahead of time.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<SpawnAgentArgs>();

    public override ToolAttributes Attributes => new() { ReadOnly = false, NoEffect = false };

    public override ValueTask<string?> GetPromptAsync(ToolContext context) =>
        ValueTask.FromResult<string?>(
            "Use spawn_agent when a task needs a pre-defined agent persona loaded from a JSON file. " +
            "The template_path must point to a valid .json file with 'id', 'systemPrompt', and optional 'tools'/'runtime' sections.");

    protected override async Task<ToolResult> ExecuteAsync(
        SpawnAgentArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
            return ToolResult.Fail("No model ID configured for spawn_agent.");

        // ── Step 1: Resolve path (L6) ─────────────────────────────────────────
        var baseDir = context.SandboxOptions?.WorkingDirectory ?? Directory.GetCurrentDirectory();
        var resolvedPath = Path.IsPathRooted(args.TemplatePath)
            ? args.TemplatePath
            : Path.GetFullPath(args.TemplatePath, baseDir);

        // ── Step 2: Parse template eagerly (L5) ───────────────────────────────
        LoadedTemplate template;
        try
        {
            template = TemplateFileLoader.LoadFromFile(resolvedPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            return ToolResult.Fail($"Failed to load template from '{resolvedPath}': {ex.Message}");
        }

        // ── Step 3: Run sub-agent ─────────────────────────────────────────────
        var parentEventBus = context.Agent?.EventBus;

        var result = await TemplateAgentRunner.RunAsync(new TemplateRunRequest
        {
            Template = template,
            Prompt = args.Prompt,
            WorkDir = args.WorkDir,
            MaxIterationsOverride = args.MaxIterations,
            ModelProvider = _modelProvider,
            ModelId = _modelId,
            ToolRegistry = _toolRegistry,
            SandboxFactory = _sandboxFactory,
            LoggerFactory = _loggerFactory,
            ParentSandboxOptions = context.SandboxOptions,
            ParentAgentId = context.AgentId,
            ToolCallId = context.CallId,
            ParentEventBus = parentEventBus,
            SkillsPaths = _skillsPaths,
        }, cancellationToken);

        if (!result.Success)
            return ToolResult.Fail(result.Error ?? $"spawn_agent '{template.Definition.Id}' failed.");

        // ── Step 4: Return result with token usage (L7) ───────────────────────
        return ToolResult.Ok(new
        {
            summary = result.Summary,
            templateId = template.Definition.Id,
            stopReason = result.StopReason,
            tokenUsage = result.TokenUsage == null ? null : new
            {
                input = result.TokenUsage.InputTokens,
                output = result.TokenUsage.OutputTokens,
                total = result.TokenUsage.TotalTokens,
            },
            toolsUsed = result.ToolsUsed,
        });
    }
}
