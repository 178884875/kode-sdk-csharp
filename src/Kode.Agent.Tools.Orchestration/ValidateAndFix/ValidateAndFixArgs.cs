using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

[GenerateToolSchema]
public class ValidateAndFixArgs
{
    [ToolParameter(Description = "The task to execute and then validate")]
    public required string Task { get; init; }

    [ToolParameter(Description =
        "Validation criteria passed to a separate validator sub-agent after task execution. " +
        "Be explicit about what constitutes pass vs fail.")]
    public required string ValidationCriteria { get; init; }

    [ToolParameter(Description = "Max fix rounds after initial validation failure (default 2, max 4).", Required = false)]
    public int MaxFixRounds { get; init; } = 2;

    [ToolParameter(Description = "Tools for the execution and fix sub-agents. Defaults to read-only tools.", Required = false)]
    public IReadOnlyList<string>? Tools { get; init; }

    [ToolParameter(Description = "Working directory override.", Required = false)]
    public string? WorkDir { get; init; }

    [ToolParameter(Description = "Max iterations per sub-agent attempt (default 12, max 50).", Required = false)]
    public int MaxIterationsPerAttempt { get; init; } = 12;

    [ToolParameter(Description =
        "Max context tokens for each sub-agent attempt (default 80000). " +
        "Increase to 120000–160000 when the task reads many large files.",
        Required = false)]
    public int MaxContextTokens { get; init; } = 80_000;

    [ToolParameter(Description =
        "Fixed: use MaxIterationsPerAttempt/MaxContextTokens as-is. " +
        "Auto: estimate complexity from the task and set both values automatically.",
        Required = false)]
    public MaxIterationsMode MaxIterationsMode { get; init; } = MaxIterationsMode.Fixed;
}
