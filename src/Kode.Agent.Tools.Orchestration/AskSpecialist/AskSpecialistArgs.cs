using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

[GenerateToolSchema]
public class AskSpecialistArgs
{
    [ToolParameter(Description = "The task for the specialist to complete")]
    public required string Task { get; init; }

    [ToolParameter(Description =
        "Description of the specialist role to adopt (e.g. 'a senior security engineer reviewing code for vulnerabilities'). " +
        "This becomes the sub-agent's identity and shapes how it approaches the task.")]
    public required string SpecialistRole { get; init; }

    [ToolParameter(Description = "Tools to grant the specialist sub-agent. Defaults to read-only tools.", Required = false)]
    public IReadOnlyList<string>? Tools { get; init; }

    [ToolParameter(Description = "Working directory override.", Required = false)]
    public string? WorkDir { get; init; }

    [ToolParameter(Description = "Max iterations for the specialist sub-agent (default 12, max 50).", Required = false)]
    public int MaxIterations { get; init; } = 12;

    [ToolParameter(Description =
        "Max context tokens for the specialist sub-agent (default 80000). " +
        "Increase to 120000–160000 when the task reads many large files.",
        Required = false)]
    public int MaxContextTokens { get; init; } = 80_000;

    [ToolParameter(Description =
        "Fixed: use MaxIterations/MaxContextTokens as-is. " +
        "Auto: estimate complexity from the task and set both values automatically.",
        Required = false)]
    public MaxIterationsMode MaxIterationsMode { get; init; } = MaxIterationsMode.Fixed;
}
