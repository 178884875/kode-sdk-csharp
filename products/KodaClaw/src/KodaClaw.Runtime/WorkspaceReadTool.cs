using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace KodaClaw.Runtime;

/// <summary>
/// Tool that reads a workspace protocol file on demand.
/// Complements workspace_protocol_update by allowing the Agent to inspect current content
/// without relying solely on the system prompt loaded at session start.
/// </summary>
public sealed class WorkspaceReadTool : ToolBase<WorkspaceReadArgs>
{
    private static readonly IReadOnlyDictionary<string, string> TargetFileMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["identity"] = KodaClawWorkspaceLayout.IdentityFile,
            ["soul"] = KodaClawWorkspaceLayout.SoulFile,
            ["user"] = KodaClawWorkspaceLayout.UserFile,
            ["memory"] = KodaClawWorkspaceLayout.MemoryFile,
            ["agents"] = KodaClawWorkspaceLayout.AgentsFile,
            ["heartbeat"] = KodaClawWorkspaceLayout.HeartbeatFile,
        };

    private readonly IWorkspaceService _workspaceService;

    public WorkspaceReadTool(IWorkspaceService workspaceService)
    {
        ArgumentNullException.ThrowIfNull(workspaceService);
        _workspaceService = workspaceService;
    }

    public override string Name => "workspace_read";

    public override string Description =>
        "Read a workspace protocol file or the daily memory log on demand. " +
        "Use target=identity/soul/user/memory/agents/heartbeat to read the corresponding protocol file. " +
        "Use target=daily_memory to read today's memory log (workspace/memory/YYYY-MM-DD.md). " +
        "Returns the file content if it exists, or indicates that the file has not been created yet.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<WorkspaceReadArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = true,
        RequiresApproval = false,
    };

    protected override async Task<ToolResult> ExecuteAsync(
        WorkspaceReadArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var workspaceDir = Path.Combine(
            _workspaceService.RootPath,
            KodaClawWorkspaceLayout.WorkspaceDirectory);

        string relativePath;
        string filePath;

        if (string.Equals(args.Target, "daily_memory", StringComparison.OrdinalIgnoreCase))
        {
            var today = DateTimeOffset.Now.ToString("yyyy-MM-dd");
            relativePath = $"workspace/memory/{today}.md";
            filePath = Path.Combine(workspaceDir, "memory", $"{today}.md");
        }
        else if (TargetFileMap.TryGetValue(args.Target, out var fileName))
        {
            relativePath = $"{KodaClawWorkspaceLayout.WorkspaceDirectory}/{fileName}";
            filePath = Path.Combine(workspaceDir, fileName);
        }
        else
        {
            var valid = string.Join(", ", TargetFileMap.Keys) + ", daily_memory";
            return ToolResult.Fail($"Unknown target '{args.Target}'. Valid values: {valid}.");
        }

        if (!File.Exists(filePath))
        {
            return ToolResult.Ok(new { target = args.Target, path = relativePath, exists = false, content = (string?)null });
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ToolResult.Ok(new { target = args.Target, path = relativePath, exists = true, content });
    }
}

/// <summary>
/// Arguments for the workspace_read tool.
/// </summary>
public sealed class WorkspaceReadArgs
{
    [ToolParameter(Description = "The workspace file to read. One of: identity, soul, user, memory, agents, heartbeat, daily_memory.")]
    public required string Target { get; init; }
}
