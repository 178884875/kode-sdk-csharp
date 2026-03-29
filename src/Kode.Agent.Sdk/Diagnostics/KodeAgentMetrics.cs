using System.Diagnostics.Metrics;

namespace Kode.Agent.Sdk.Diagnostics;

/// <summary>
/// .NET built-in Meter for Agent runtime numeric metrics.
/// Consumers: dotnet-counters, OpenTelemetry, MeterListener.
/// </summary>
public static class KodeAgentMetrics
{
    public const string MeterName = "Kode.Agent";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // ── Run ──
    public static readonly Counter<long> RunsStarted =
        Meter.CreateCounter<long>("kode.agent.runs.started", description: "Agent runs started");

    public static readonly Counter<long> RunsCompleted =
        Meter.CreateCounter<long>("kode.agent.runs.completed", description: "Agent runs completed");

    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>("kode.agent.run.duration", "ms", "Agent run duration");

    // ── Step ──
    public static readonly Counter<long> StepsCompleted =
        Meter.CreateCounter<long>("kode.agent.steps.completed", description: "Agent steps completed");

    public static readonly Histogram<double> StepDuration =
        Meter.CreateHistogram<double>("kode.agent.step.duration", "ms", "Agent step duration");

    // ── Token usage ──
    public static readonly Counter<long> TokensInput =
        Meter.CreateCounter<long>("kode.agent.tokens.input", "tokens", "Input tokens consumed");

    public static readonly Counter<long> TokensOutput =
        Meter.CreateCounter<long>("kode.agent.tokens.output", "tokens", "Output tokens consumed");

    // ── Model requests ──
    public static readonly Counter<long> ModelRequests =
        Meter.CreateCounter<long>("kode.agent.model.requests", description: "Model API requests");

    public static readonly Counter<long> ModelErrors =
        Meter.CreateCounter<long>("kode.agent.model.errors", description: "Model API errors");

    public static readonly Histogram<double> ModelRequestDuration =
        Meter.CreateHistogram<double>("kode.agent.model.request.duration", "ms", "Model request duration");

    // ── Tool execution ──
    public static readonly Counter<long> ToolExecutions =
        Meter.CreateCounter<long>("kode.agent.tool.executions", description: "Tool executions");

    public static readonly Counter<long> ToolErrors =
        Meter.CreateCounter<long>("kode.agent.tool.errors", description: "Tool execution errors");

    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>("kode.agent.tool.duration", "ms", "Tool execution duration");

    // ── Context compression ──
    public static readonly Counter<long> ContextCompressions =
        Meter.CreateCounter<long>("kode.agent.context.compressions", description: "Context compression events");
}
