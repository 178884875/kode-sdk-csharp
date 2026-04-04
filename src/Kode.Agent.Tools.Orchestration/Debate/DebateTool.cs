using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration.Internal;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Runs adversarial argumentation: a proponent sub-agent argues FOR a proposition,
/// an opponent sub-agent argues AGAINST it (seeing the proponent's argument),
/// then a judge sub-agent evaluates both sides and delivers a verdict.
///
/// Multi-round debates run the proponent and opponent alternately, each seeing
/// the other's latest argument before responding.
///
/// Use for high-stakes decisions where a single perspective risks blind spots:
/// architecture selection, risk assessment, significant refactors.
/// </summary>
[Tool("debate")]
[ToolAttributes(ReadOnly = true, NoEffect = true)]
public sealed class DebateTool : ToolBase<DebateArgs>
{
    private readonly IModelProvider _modelProvider;
    private readonly string _modelId;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    public DebateTool(
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

    public override string Name => "debate";

    public override string Description =>
        "Run an adversarial debate on a proposition. " +
        "A proponent argues FOR, an opponent argues AGAINST, then a judge delivers a verdict. " +
        "Multi-round debates let each side respond to the other's latest argument. " +
        "Use for high-stakes decisions (architecture selection, risk assessment) " +
        "where a single perspective may miss important counter-arguments.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<DebateArgs>();

    public override ToolAttributes Attributes => new() { ReadOnly = true, NoEffect = true };

    public override ValueTask<string?> GetPromptAsync(ToolContext context) =>
        ValueTask.FromResult<string?>(
            "Use debate for important decisions where you want to surface both pros and cons. " +
            "State the Topic as a clear proposition (e.g. 'We should adopt approach X'). " +
            "Provide ContextInfo with relevant background. " +
            "For most decisions Rounds=1 is sufficient; use Rounds=2 for complex trade-offs.");

    protected override async Task<ToolResult> ExecuteAsync(
        DebateArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
            return ToolResult.Fail("No model ID configured for debate sub-agents.");

        var rounds = Math.Clamp(args.Rounds, 1, 3);
        var debateRounds = new List<object>();

        string? lastProponentArg = null;
        string? lastOpponentArg = null;

        for (var round = 1; round <= rounds; round++)
        {
            // ── proponent ─────────────────────────────────────────────────
            var proponentTask = BuildSideTask(
                "FOR", args.Topic, args.ContextInfo, round, lastOpponentArg);

            var proResult = await RunDebaterAsync(proponentTask, args, context, cancellationToken);
            lastProponentArg = proResult.Success ? proResult.Summary : $"(failed: {proResult.Error})";

            debateRounds.Add(new
            {
                round, side = "proponent",
                success = proResult.Success,
                argument = lastProponentArg,
            });

            // ── opponent ──────────────────────────────────────────────────
            var opponentTask = BuildSideTask(
                "AGAINST", args.Topic, args.ContextInfo, round, lastProponentArg);

            var opResult = await RunDebaterAsync(opponentTask, args, context, cancellationToken);
            lastOpponentArg = opResult.Success ? opResult.Summary : $"(failed: {opResult.Error})";

            debateRounds.Add(new
            {
                round, side = "opponent",
                success = opResult.Success,
                argument = lastOpponentArg,
            });
        }

        // ── judge ─────────────────────────────────────────────────────────
        var judgeTask = BuildJudgeTask(args.Topic, args.ContextInfo, lastProponentArg, lastOpponentArg);
        var judgeResult = await SubAgentRunner.RunAsync(new SubAgentRequest
        {
            Task = judgeTask,
            Tools = [],
            AllowNoTools = true,
            MaxIterations = 5,
            ParentSandboxOptions = context.SandboxOptions,
            ModelProvider = _modelProvider,
            ModelId = _modelId,
            ToolRegistry = _toolRegistry,
            SandboxFactory = _sandboxFactory,
            LoggerFactory = _loggerFactory,
        }, cancellationToken);

        return ToolResult.Ok(new
        {
            topic = args.Topic,
            verdict = judgeResult.Success ? judgeResult.Summary : null,
            judgeError = judgeResult.Success ? null : judgeResult.Error,
            rounds = debateRounds,
            succeeded = judgeResult.Success,
        });
    }

    private Task<SubAgentResult> RunDebaterAsync(
        string task, DebateArgs args, ToolContext context, CancellationToken ct) =>
        SubAgentRunner.RunAsync(new SubAgentRequest
        {
            Task = task,
            Tools = args.Tools is { Count: > 0 } ? args.Tools : [],
            AllowNoTools = true,
            MaxIterations = 8,
            ParentSandboxOptions = context.SandboxOptions,
            ModelProvider = _modelProvider,
            ModelId = _modelId,
            ToolRegistry = _toolRegistry,
            SandboxFactory = _sandboxFactory,
            LoggerFactory = _loggerFactory,
        }, ct);

    private static string BuildSideTask(
        string stance, string topic, string? context, int round, string? previousOpponentArg)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You are an expert debater. Argue {stance} the following proposition.");
        sb.AppendLine();
        sb.AppendLine($"Proposition: {topic}");

        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine();
            sb.AppendLine($"Background context: {context}");
        }

        if (round > 1 && !string.IsNullOrWhiteSpace(previousOpponentArg))
        {
            sb.AppendLine();
            sb.AppendLine("The opposing side argued:");
            sb.AppendLine(previousOpponentArg);
            sb.AppendLine();
            sb.AppendLine("Respond to their argument and reinforce your position.");
        }

        sb.AppendLine();
        sb.AppendLine("Present your strongest arguments. Be concise and specific (under 300 words).");
        return sb.ToString();
    }

    private static string BuildJudgeTask(
        string topic, string? context, string? proponentArg, string? opponentArg)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are an impartial judge evaluating a debate. Deliver a fair verdict.");
        sb.AppendLine();
        sb.AppendLine($"Proposition: {topic}");

        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine();
            sb.AppendLine($"Background context: {context}");
        }

        sb.AppendLine();
        sb.AppendLine("## Proponent's argument (FOR):");
        sb.AppendLine(proponentArg ?? "(no argument)");
        sb.AppendLine();
        sb.AppendLine("## Opponent's argument (AGAINST):");
        sb.AppendLine(opponentArg ?? "(no argument)");
        sb.AppendLine();
        sb.AppendLine("Evaluate both arguments on merit. State:");
        sb.AppendLine("1. Which side made the stronger case and why");
        sb.AppendLine("2. The key considerations a decision-maker should weigh");
        sb.AppendLine("3. Your recommendation (accept / reject / conditional)");
        return sb.ToString();
    }
}
