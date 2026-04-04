using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration.Internal;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Routes a task to a sub-agent that adopts a specified specialist role via a
/// custom system prompt. Use when the task benefits from a domain-specific
/// perspective (security review, architecture critique, legal compliance check)
/// without polluting the main agent's persona or context.
/// </summary>
[Tool("ask_specialist")]
[ToolAttributes(ReadOnly = true, NoEffect = true)]
public sealed class AskSpecialistTool : ToolBase<AskSpecialistArgs>
{
    private readonly IModelProvider _modelProvider;
    private readonly string _modelId;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    public AskSpecialistTool(
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

    public override string Name => "ask_specialist";

    public override string Description =>
        "Ask a specialist sub-agent to complete a task from a specific expert perspective. " +
        "The sub-agent adopts the given specialist role (e.g. 'security engineer', 'database architect') " +
        "and approaches the task accordingly. " +
        "Use this when the task requires domain expertise without changing the current agent's persona.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<AskSpecialistArgs>();

    public override ToolAttributes Attributes => new() { ReadOnly = true, NoEffect = true };

    public override ValueTask<string?> GetPromptAsync(ToolContext context) =>
        ValueTask.FromResult<string?>(
            "Use ask_specialist when a task needs a specific expert perspective: " +
            "security reviews, architecture critiques, compliance checks, performance analysis. " +
            "Describe the specialist role clearly — it becomes their identity and shapes their analysis.");

    protected override async Task<ToolResult> ExecuteAsync(
        AskSpecialistArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
            return ToolResult.Fail("No model ID configured for ask_specialist sub-agent.");

        if (string.IsNullOrWhiteSpace(args.SpecialistRole))
            return ToolResult.Fail("SpecialistRole must not be empty.");

        var childWorkDir = args.WorkDir ?? context.SandboxOptions?.WorkingDirectory;
        var systemPrompt = BuildSpecialistSystemPrompt(args.SpecialistRole, childWorkDir);

        var result = await SubAgentRunner.RunAsync(new SubAgentRequest
        {
            Task = args.Task,
            WorkDir = args.WorkDir,
            Tools = args.Tools,
            MaxIterations = args.MaxIterations,
            ParentSandboxOptions = context.SandboxOptions,
            ModelProvider = _modelProvider,
            ModelId = _modelId,
            ToolRegistry = _toolRegistry,
            SandboxFactory = _sandboxFactory,
            LoggerFactory = _loggerFactory,
            SystemPromptOverride = systemPrompt,
        }, cancellationToken);

        if (!result.Success)
            return ToolResult.Fail(result.Error ?? "Specialist sub-agent failed.");

        return ToolResult.Ok(new
        {
            summary = result.Summary,
            specialistRole = args.SpecialistRole,
            stopReason = result.StopReason,
        });
    }

    private static string BuildSpecialistSystemPrompt(string role, string? workDir)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You are {role}.");

        if (!string.IsNullOrWhiteSpace(workDir))
            sb.AppendLine($"Working directory: {workDir}");

        sb.AppendLine();
        sb.AppendLine("Guidelines:");
        sb.AppendLine("- Apply your specialist expertise to the task. Be systematic and thorough.");
        sb.AppendLine("- Do NOT send messages, modify workspace files outside the task scope, or create approvals.");
        sb.AppendLine("- Produce a concise, expert-level response under 500 words.");
        sb.AppendLine("- If something cannot be determined with the available information, say so explicitly.");

        return sb.ToString();
    }
}
