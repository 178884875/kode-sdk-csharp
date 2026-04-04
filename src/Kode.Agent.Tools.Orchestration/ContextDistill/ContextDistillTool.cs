using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration.Internal;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Passes large content to a sub-agent for distillation, returning only the
/// key information relevant to a focus question. No tool calls are made —
/// the sub-agent reasons over the provided text purely in-context.
///
/// Use when a tool result, file content, or accumulated context is too large
/// to pass directly to the next step in a pipeline or decision.
/// </summary>
[Tool("context_distill")]
[ToolAttributes(ReadOnly = true, NoEffect = true)]
public sealed class ContextDistillTool : ToolBase<ContextDistillArgs>
{
    private readonly IModelProvider _modelProvider;
    private readonly string _modelId;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    public ContextDistillTool(
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

    public override string Name => "context_distill";

    public override string Description =>
        "Distill large content (tool output, file contents, logs) into a focused summary. " +
        "A sub-agent reads the content and extracts only what is relevant to the focus question, " +
        "returning a concise summary that fits comfortably in the current context window. " +
        "No tool calls are made — purely text reasoning.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<ContextDistillArgs>();

    public override ToolAttributes Attributes => new() { ReadOnly = true, NoEffect = true };

    public override ValueTask<string?> GetPromptAsync(ToolContext context) =>
        ValueTask.FromResult<string?>(
            "Use context_distill when you have large text (logs, file dumps, tool output) " +
            "that exceeds what fits in the next prompt. Pass it here with a specific focus question " +
            "to get a concise targeted summary back.");

    protected override async Task<ToolResult> ExecuteAsync(
        ContextDistillArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
            return ToolResult.Fail("No model ID configured for context_distill sub-agent.");

        if (string.IsNullOrWhiteSpace(args.Content))
            return ToolResult.Fail("Content must not be empty.");

        if (string.IsNullOrWhiteSpace(args.FocusQuestion))
            return ToolResult.Fail("FocusQuestion must not be empty.");

        var maxWords = Math.Clamp(args.MaxOutputWords, 50, 1000);
        var task = BuildDistillTask(args.Content, args.FocusQuestion, maxWords);

        var result = await SubAgentRunner.RunAsync(new SubAgentRequest
        {
            Task = task,
            Tools = [],              // no tool calls — pure reasoning
            AllowNoTools = true,
            MaxIterations = 3,       // single response expected
            ParentSandboxOptions = context.SandboxOptions,
            ModelProvider = _modelProvider,
            ModelId = _modelId,
            ToolRegistry = _toolRegistry,
            SandboxFactory = _sandboxFactory,
            LoggerFactory = _loggerFactory,
        }, cancellationToken);

        if (!result.Success)
            return ToolResult.Fail(result.Error ?? "Distillation sub-agent failed.");

        return ToolResult.Ok(new
        {
            distillation = result.Summary,
            focusQuestion = args.FocusQuestion,
        });
    }

    private static string BuildDistillTask(string content, string focusQuestion, int maxWords) =>
        $"""
         You are a precise content distiller.

         Focus question: {focusQuestion}

         Content to distill:
         ---
         {content}
         ---

         Extract and summarise only the information relevant to the focus question above.
         Keep your response under {maxWords} words. Be specific and factual. Omit anything unrelated.
         """;
}
