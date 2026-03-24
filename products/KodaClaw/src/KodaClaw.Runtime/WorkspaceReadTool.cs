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
    private readonly IThreadBindingRepository? _threadBindingRepository;

    public WorkspaceReadTool(
        IWorkspaceService workspaceService,
        IThreadBindingRepository? threadBindingRepository = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceService);
        _workspaceService = workspaceService;
        _threadBindingRepository = threadBindingRepository;
    }

    public override string Name => "workspace_read";

    public override string Description =>
        "Read a workspace protocol file, daily memory log, or channel binding list on demand. " +
        "Use target=identity/soul/user/memory/agents/heartbeat to read the corresponding protocol file. " +
        "Use target=daily_memory to read today's memory log (workspace/memory/YYYY-MM-DD.md). " +
        "Use target=channels to list all configured channel bindings with their BindingIds — " +
        "always call this before writing a heartbeat section that includes channels or delivery-mode. " +
        "Returns the file content or binding list if available, or indicates nothing is configured yet.";

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
        if (string.Equals(args.Target, "channels", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadChannelBindingsAsync(cancellationToken);
        }

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
            var valid = string.Join(", ", TargetFileMap.Keys) + ", daily_memory, channels";
            return ToolResult.Fail($"Unknown target '{args.Target}'. Valid values: {valid}.");
        }

        if (!File.Exists(filePath))
        {
            return ToolResult.Ok(new { target = args.Target, path = relativePath, exists = false, content = (string?)null });
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ToolResult.Ok(new { target = args.Target, path = relativePath, exists = true, content });
    }

    private async Task<ToolResult> ReadChannelBindingsAsync(CancellationToken cancellationToken)
    {
        if (_threadBindingRepository is null)
        {
            return ToolResult.Ok(new { target = "channels", bindings = Array.Empty<object>(), note = "Channel bindings are not available in this session." });
        }

        var bindings = await _threadBindingRepository.ListAsync(cancellationToken: cancellationToken);
        if (bindings.Count == 0)
        {
            return ToolResult.Ok(new { target = "channels", bindings = Array.Empty<object>(), note = "No channel conversations found. If you have connected a Telegram or Feishu account, send a message to the bot first — that creates the conversation binding and generates a BindingId you can use in HEARTBEAT.md." });
        }

        var items = bindings.Select(b => new
        {
            bindingId = b.Id,
            kind = b.ConnectorKind.ToString(),
            threadType = b.ThreadType.ToString(),
            displayName = b.ChannelIdentity.DisplayName ?? b.ChannelIdentity.Username ?? b.ChannelIdentity.Id,
        }).ToArray();

        return ToolResult.Ok(new { target = "channels", bindings = items });
    }
}

/// <summary>
/// Arguments for the workspace_read tool.
/// </summary>
public sealed class WorkspaceReadArgs
{
    [ToolParameter(Description = "The workspace file or data source to read. One of: identity, soul, user, memory, agents, heartbeat, daily_memory, channels. Use 'channels' to list all configured channel bindings with their BindingIds before editing a heartbeat section.")]
    public required string Target { get; init; }
}
