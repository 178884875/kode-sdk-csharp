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

    [ToolParameter(Description = "Max iterations for the specialist sub-agent (default 20, max 50).", Required = false)]
    public int MaxIterations { get; init; } = 20;
}
