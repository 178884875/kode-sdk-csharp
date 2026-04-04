using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

[GenerateToolSchema]
public class ContextDistillArgs
{
    [ToolParameter(Description = "The raw content to distill (tool output, file contents, logs, etc.)")]
    public required string Content { get; init; }

    [ToolParameter(Description = "The question or goal that guides what to keep in the distillation")]
    public required string FocusQuestion { get; init; }

    [ToolParameter(Description = "Maximum output length in words (default 300).", Required = false)]
    public int MaxOutputWords { get; init; } = 300;
}
