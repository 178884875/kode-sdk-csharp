using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration.Internal;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Runs a self-contained investigation task in an isolated sub-agent whose entire
/// message history is discarded afterwards. Only the final summary returns to the
/// parent agent, protecting the parent's context window from large intermediate results.
///
/// Design references:
///   - Anthropic "Building effective agents" — Orchestrator-Worker pattern
///   - MemGPT (arxiv 2310.08560) — hierarchical context management
/// </summary>
[Tool("isolate_task")]
[ToolAttributes(ReadOnly = true, NoEffect = true)]
public sealed class IsolateTaskTool : ToolBase<IsolateTaskArgs>
{
    private readonly IModelProvider _modelProvider;
    private readonly string _modelId;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    public IsolateTaskTool(
        IModelProvider modelProvider,
        string modelId,
        IToolRegistry toolRegistry,
        ISandboxFactory sandboxFactory,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        _modelProvider = modelProvider;
        _modelId = modelId;
        _toolRegistry = toolRegistry;
        _sandboxFactory = sandboxFactory;
        _loggerFactory = loggerFactory;
    }

    public override string Name => "isolate_task";

    public override string Description =>
        "Run a multi-step investigation task in an isolated sub-agent. " +
        "The sub-agent has its own context window; only its final summary is returned here. " +
        "Use this when a task requires many tool calls (reading files, searching code, browsing) " +
        "that would otherwise fill the current context window. " +
        "The sub-agent cannot send messages, modify workspace, or create approvals.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<IsolateTaskArgs>();

    public override ToolAttributes Attributes => new() { ReadOnly = true, NoEffect = true };

    public override ValueTask<string?> GetPromptAsync(ToolContext context) =>
        ValueTask.FromResult<string?>(
            "Use isolate_task when you need to do deep research that requires many tool calls " +
            "(e.g. reading 10+ files, analyzing a codebase, scraping multiple pages). " +
            "The sub-agent result is a plain-text summary — ask for exactly what you need in the task description. " +
            "Specify workDir when the task is focused on a directory outside the current workspace.");

    protected override async Task<ToolResult> ExecuteAsync(
        IsolateTaskArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
            return ToolResult.Fail("No model ID configured for isolate_task sub-agent.");

        var result = await SubAgentRunner.RunAsync(new SubAgentRequest
        {
            Task = args.Task,
            WorkDir = args.WorkDir,
            Tools = args.Tools,
            MaxIterations = args.MaxIterations,
            MaxContextTokens = args.MaxContextTokens,
            MaxIterationsMode = args.MaxIterationsMode,
            ParentSandboxOptions = context.SandboxOptions,
            ModelProvider = _modelProvider,
            ModelId = _modelId,
            ToolRegistry = _toolRegistry,
            SandboxFactory = _sandboxFactory,
            LoggerFactory = _loggerFactory,
            ParentEventBus = context.Agent?.EventBus,
            Label = "isolate_task",
            ToolCallId = context.CallId,
        }, cancellationToken);

        if (!result.Success)
            return ToolResult.Fail(result.Error ?? "Sub-agent failed.");

        return ToolResult.Ok(new
        {
            summary = result.Summary,
            stopReason = result.StopReason,
            toolsUsed = result.ToolsUsed,
        });
    }
}
