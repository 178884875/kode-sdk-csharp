using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace KodaClaw.Runtime;

/// <summary>
/// Tool that appends a memory entry to the daily memory log in the workspace.
/// </summary>
public sealed class WorkspaceMemoryAppendTool : ToolBase<WorkspaceMemoryAppendArgs>
{
    private readonly IWorkspaceService _workspaceService;

    public WorkspaceMemoryAppendTool(IWorkspaceService workspaceService)
    {
        ArgumentNullException.ThrowIfNull(workspaceService);
        _workspaceService = workspaceService;
    }

    public override string Name => "workspace_memory_append";

    public override string Description =>
        "Append a memory entry to today's daily memory log in the workspace. " +
        "Use this to record important facts, decisions, or insights worth remembering across sessions. " +
        "Entries are stored in workspace/memory/YYYY-MM-DD.md and consolidated into MEMORY.md by nightly automation.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<WorkspaceMemoryAppendArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        RequiresApproval = false,
    };

    protected override async Task<ToolResult> ExecuteAsync(
        WorkspaceMemoryAppendArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var date = string.IsNullOrWhiteSpace(args.Date)
            ? DateTimeOffset.Now.ToString("yyyy-MM-dd")
            : args.Date;

        var memoryDirectory = Path.Combine(
            _workspaceService.RootPath,
            KodaClawWorkspaceLayout.WorkspaceDirectory,
            "memory");

        Directory.CreateDirectory(memoryDirectory);

        var filePath = Path.Combine(memoryDirectory, $"{date}.md");
        var timestamp = DateTimeOffset.Now.ToString("HH:mm");
        var entry = $"\n<!-- {timestamp} -->\n{args.Content.TrimEnd()}\n";

        await File.AppendAllTextAsync(filePath, entry, cancellationToken);

        Emit(context, "workspace_memory_appended", new { date, path = filePath });

        await _workspaceService.TryCommitWorkspaceAsync(
            $"workspace(memory)[agent]: append daily {date}",
            cancellationToken);

        return ToolResult.Ok(new { ok = true, date, path = filePath });
    }
}

/// <summary>
/// Arguments for the workspace_memory_append tool.
/// </summary>
public sealed class WorkspaceMemoryAppendArgs
{
    [ToolParameter(Description = "The memory entry to append. Markdown text describing a fact, decision, or insight worth remembering.")]
    public required string Content { get; init; }

    [ToolParameter(Description = "The date to write the memory for, in yyyy-MM-dd format. Defaults to today if omitted.", Required = false)]
    public string? Date { get; init; }
}
