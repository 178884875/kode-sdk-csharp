using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

[GenerateToolSchema]
public class SpawnAgentArgs
{
    [ToolParameter(Description =
        "Path to the JSON template file that defines the sub-agent's identity, tools, and runtime configuration. " +
        "Absolute paths are used as-is; relative paths are resolved against the current working directory.")]
    public required string TemplatePath { get; init; }

    [ToolParameter(Description = "Task description passed to the sub-agent as its user input.")]
    public required string Prompt { get; init; }

    [ToolParameter(Description = "Working directory override for the sub-agent sandbox. Defaults to parent's working directory.", Required = false)]
    public string? WorkDir { get; init; }

    [ToolParameter(Description = "Override the maximum number of iterations from the template (1–100).", Required = false)]
    public int? MaxIterations { get; init; }
}
