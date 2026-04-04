using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

[GenerateToolSchema]
public class FanOutFanInArgs
{
    [ToolParameter(Description = "Independent research tasks to run in parallel (fan-out)")]
    public required IReadOnlyList<ResearchTask> Tasks { get; init; }

    [ToolParameter(Description =
        "Synthesis task for the fan-in sub-agent. All fan-out summaries are prepended as context " +
        "before this task description.")]
    public required string SynthesisTask { get; init; }

    [ToolParameter(Description = "Max concurrent fan-out sub-agents (0 = unlimited).", Required = false)]
    public int MaxConcurrency { get; init; } = 0;

    [ToolParameter(Description = "Tools for the synthesis sub-agent. Defaults to read-only tools.", Required = false)]
    public IReadOnlyList<string>? SynthesisTools { get; init; }

    [ToolParameter(Description = "Max iterations for the synthesis sub-agent (default 20).", Required = false)]
    public int SynthesisMaxIterations { get; init; } = 20;
}
