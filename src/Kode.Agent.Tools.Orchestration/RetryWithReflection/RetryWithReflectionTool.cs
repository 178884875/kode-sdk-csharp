using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration.Internal;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Runs a task in an isolated sub-agent and, on failure, retries with the error
/// and a reflection prompt prepended — asking the sub-agent to diagnose what went
/// wrong and approach the problem differently.
///
/// Ideal for unattended HEARTBEAT automation stages where transient errors
/// (wrong path, unexpected output format, minor tool misuse) should self-correct
/// rather than abort the entire pipeline.
///
/// Design reference: Anthropic "Building effective agents" — Reflection pattern
/// </summary>
[Tool("retry_with_reflection")]
[ToolAttributes(ReadOnly = true, NoEffect = true)]
public sealed class RetryWithReflectionTool : ToolBase<RetryWithReflectionArgs>
{
    private readonly IModelProvider _modelProvider;
    private readonly string _modelId;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    public RetryWithReflectionTool(
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

    public override string Name => "retry_with_reflection";

    public override string Description =>
        "Run a task in an isolated sub-agent and automatically retry on failure. " +
        "Each retry receives the previous error and a reflection prompt so the sub-agent " +
        "can diagnose what went wrong and approach the problem differently. " +
        "Use this for tasks that may fail due to transient issues or minor misunderstandings " +
        "that a second attempt can self-correct. " +
        "The sub-agent cannot send messages, modify workspace, or create approvals.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<RetryWithReflectionArgs>();

    public override ToolAttributes Attributes => new() { ReadOnly = true, NoEffect = true };

    public override ValueTask<string?> GetPromptAsync(ToolContext context) =>
        ValueTask.FromResult<string?>(
            "Use retry_with_reflection for unattended tasks that may fail due to transient issues " +
            "(wrong path, unexpected output format, minor tool misuse). " +
            "Set maxRetries=1 for quick tasks, 2-3 for complex ones. " +
            "Even on final failure the result includes all attempt records so you can diagnose the issue.");

    protected override async Task<ToolResult> ExecuteAsync(
        RetryWithReflectionArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
            return ToolResult.Fail("No model ID configured for retry_with_reflection sub-agent.");

        var totalAttempts = Math.Clamp(args.MaxRetries, 0, 5) + 1;
        var attemptRecords = new List<object>(totalAttempts);
        string? previousError = null;

        for (var attempt = 1; attempt <= totalAttempts; attempt++)
        {
            var task = attempt == 1
                ? args.Task
                : BuildRetryTask(args.Task, attempt - 1, previousError!);

            var result = await SubAgentRunner.RunAsync(new SubAgentRequest
            {
                Task = task,
                WorkDir = args.WorkDir,
                Tools = args.Tools,
                MaxIterations = args.MaxIterationsPerAttempt,
                MaxContextTokens = args.MaxContextTokens,
                MaxIterationsMode = args.MaxIterationsMode,
                ParentSandboxOptions = context.SandboxOptions,
                ModelProvider = _modelProvider,
                ModelId = _modelId,
                ToolRegistry = _toolRegistry,
                SandboxFactory = _sandboxFactory,
                LoggerFactory = _loggerFactory,
                ParentEventBus = context.Agent?.EventBus,
                Label = $"retry_with_reflection:{attempt}/{totalAttempts}",
                ToolCallId = context.CallId,
            }, cancellationToken);

            if (result.Success)
            {
                attemptRecords.Add(new { attempt, success = true, summary = result.Summary });
                // Return Ok (not Fail) even here so the caller gets full attempt history
                return ToolResult.Ok(new
                {
                    success = true,
                    summary = result.Summary,
                    totalAttempts = attempt,
                    attempts = attemptRecords,
                });
            }

            previousError = result.Error;
            attemptRecords.Add(new { attempt, success = false, error = result.Error });
        }

        // All attempts exhausted — return Fail so the caller knows the task did not complete.
        // The attempt log is included in the error message for debugging.
        var lastError = previousError ?? "unknown error";
        var attemptSummary = string.Join("; ", attemptRecords.Select((r, i) =>
        {
            var rec = (dynamic)r;
            return $"attempt {i + 1}: {(rec.success ? "ok" : rec.error ?? "failed")}";
        }));
        return ToolResult.Fail(
            $"retry_with_reflection exhausted {totalAttempts} attempt(s). Last error: {lastError}. " +
            $"Attempts: [{attemptSummary}]");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string BuildRetryTask(string originalTask, int failedAttempt, string error) =>
        $"""
         Original task:
         {originalTask}

         Attempt {failedAttempt} failed with the following error:
         {error}

         Before retrying, reflect on what went wrong:
         - Was the approach correct? Were the right tools used?
         - Was there a wrong assumption about file paths, formats, or tool behaviour?
         - What should change this time to avoid the same failure?

         Now retry the original task with this understanding.
         """;
}
