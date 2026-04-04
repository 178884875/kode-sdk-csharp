using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

[GenerateToolSchema]
public class DebateArgs
{
    [ToolParameter(Description =
        "The proposition to debate (e.g. 'We should migrate the auth service from JWT to sessions').")]
    public required string Topic { get; init; }

    [ToolParameter(Description =
        "Background context injected into all debater and judge system prompts. " +
        "Include relevant constraints, codebase facts, or requirements.", Required = false)]
    public string? ContextInfo { get; init; }

    [ToolParameter(Description =
        "Number of argumentation rounds per side (default 1). " +
        "In round 2+, each side sees the other's previous argument before responding.", Required = false)]
    public int Rounds { get; init; } = 1;

    [ToolParameter(Description = "Tools for debater sub-agents. Defaults to read-only tools.", Required = false)]
    public IReadOnlyList<string>? Tools { get; init; }
}
