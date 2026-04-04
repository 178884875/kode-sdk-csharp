using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration.Internal;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Executes a multi-stage pipeline where each stage runs in an isolated sub-agent.
/// The summary from each stage is passed as context to the next stage, so only
/// refined summaries accumulate — not raw intermediate tool results.
///
/// Ideal for HEARTBEAT-style automation workflows where multiple sequential phases
/// (e.g. gather → consolidate → clean) would otherwise pollute a single context window.
///
/// Design reference: Anthropic "Building effective agents" — Pipeline pattern
/// </summary>
[Tool("pipeline")]
[ToolAttributes(ReadOnly = false, NoEffect = false)]
public sealed class PipelineTool : ToolBase<PipelineArgs>
{
    private readonly IModelProvider _modelProvider;
    private readonly string _modelId;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    public PipelineTool(
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

    public override string Name => "pipeline";

    public override string Description =>
        "Execute a multi-stage pipeline where each stage runs in an isolated sub-agent. " +
        "The output summary of each stage is automatically passed as context to the next stage. " +
        "Stages share no context window — only the summary is forwarded — preventing context accumulation. " +
        "Use this for multi-phase tasks (e.g. gather data → analyze → produce report) where " +
        "the full intermediate results would overflow the current context window.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<PipelineArgs>();

    public override ToolAttributes Attributes => new() { ReadOnly = false, NoEffect = false };

    public override ValueTask<string?> GetPromptAsync(ToolContext context) =>
        ValueTask.FromResult<string?>(
            "Use pipeline for multi-phase automation tasks (e.g. HEARTBEAT workflows). " +
            "Each stage is an isolated sub-agent — only the summary flows forward to the next stage. " +
            "Give each stage a descriptive name so context handoff is clear. " +
            "For single-phase deep research use isolate_task instead.");

    protected override async Task<ToolResult> ExecuteAsync(
        PipelineArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
            return ToolResult.Fail("No model ID configured for pipeline sub-agents.");

        if (args.Stages is not { Count: > 0 })
            return ToolResult.Fail("Pipeline must have at least one stage.");

        var stageResults = new List<object>();
        string? previousSummary = null;
        string? previousName = null;
        var completedCount = 0;

        for (var i = 0; i < args.Stages.Count; i++)
        {
            var stage = args.Stages[i];
            var stageName = stage.Name ?? $"Stage {i + 1}";

            // Prepend previous stage summary as context
            var task = previousSummary is not null
                ? $"Context from previous stage ({previousName}):\n{previousSummary}\n\n{stage.Task}"
                : stage.Task;

            var result = await SubAgentRunner.RunAsync(new SubAgentRequest
            {
                Task = task,
                WorkDir = stage.WorkDir,
                Tools = stage.Tools,
                MaxIterations = stage.MaxIterations,
                ParentSandboxOptions = context.SandboxOptions,
                ModelProvider = _modelProvider,
                ModelId = _modelId,
                ToolRegistry = _toolRegistry,
                SandboxFactory = _sandboxFactory,
                LoggerFactory = _loggerFactory,
            }, cancellationToken);

            if (result.Success)
            {
                completedCount++;
                previousSummary = result.Summary;
                previousName = stageName;

                stageResults.Add(new
                {
                    name = stageName,
                    success = true,
                    summary = result.Summary,
                    stopReason = result.StopReason,
                });
            }
            else
            {
                stageResults.Add(new
                {
                    name = stageName,
                    success = false,
                    summary = (string?)null,
                    error = result.Error,
                });

                if (args.StopOnFailure)
                    break;
            }
        }

        var allSucceeded = completedCount == args.Stages.Count;

        return ToolResult.Ok(new
        {
            stages = stageResults,
            completedStages = completedCount,
            totalStages = args.Stages.Count,
            succeeded = allSucceeded,
        });
    }
}
