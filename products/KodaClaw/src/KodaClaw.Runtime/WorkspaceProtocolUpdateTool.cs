using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace KodaClaw.Runtime;

/// <summary>
/// Tool that applies a section-level patch to a workspace protocol file.
/// The Agent decides which file and section to update based on semantic understanding.
/// </summary>
public sealed class WorkspaceProtocolUpdateTool : ToolBase<WorkspaceProtocolUpdateArgs>
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

    public WorkspaceProtocolUpdateTool(IWorkspaceService workspaceService)
    {
        ArgumentNullException.ThrowIfNull(workspaceService);
        _workspaceService = workspaceService;
    }

    public override string Name => "workspace_protocol_update";

    public override string Description =>
        "Update a section of a workspace protocol file (identity, soul, user, memory, agents, or heartbeat). " +
        "Use this when the user shares information that should permanently update their profile, " +
        "preferences, behavioral rules, long-term memory, or scheduled automation rules. " +
        "Use target=heartbeat to add or modify a ## SectionTitle automation rule in HEARTBEAT.md. " +
        "Changes to identity/soul/user/memory/agents take effect at the next session start. " +
        "Changes to heartbeat take effect immediately via the hot-sync pipeline.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<WorkspaceProtocolUpdateArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        RequiresApproval = false,
    };

    protected override async Task<ToolResult> ExecuteAsync(
        WorkspaceProtocolUpdateArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (!TargetFileMap.TryGetValue(args.Target, out var fileName))
        {
            var valid = string.Join(", ", TargetFileMap.Keys);
            return ToolResult.Fail($"Unknown target '{args.Target}'. Valid values: {valid}.");
        }

        var filePath = Path.Combine(
            _workspaceService.RootPath,
            KodaClawWorkspaceLayout.WorkspaceDirectory,
            fileName);

        var currentContent = File.Exists(filePath)
            ? await File.ReadAllTextAsync(filePath, cancellationToken)
            : GetDefaultContent(args.Target);

        var patched = ApplySectionPatch(currentContent, args.Section, args.Content);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, patched, cancellationToken);

        Emit(context, "workspace_protocol_updated", new
        {
            target = args.Target,
            section = args.Section,
            path = filePath,
            bytes = patched.Length,
        });

        return ToolResult.Ok(new { ok = true, target = args.Target, section = args.Section, path = filePath });
    }

    /// <summary>
    /// Applies a section-level patch to the given markdown content.
    /// </summary>
    public static string ApplySectionPatch(string currentContent, string? section, string newContent)
    {
        var trimmedNew = newContent.TrimEnd();

        // No section specified → replace everything after the first # title line.
        if (string.IsNullOrWhiteSpace(section))
        {
            var lines = currentContent.Split('\n');
            var titleLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("# ", StringComparison.Ordinal));
            if (titleLine is null)
            {
                return trimmedNew + "\n";
            }

            return titleLine.TrimEnd() + "\n\n" + trimmedNew + "\n";
        }

        // Section specified → locate ## {section}, replace its body.
        var sectionHeading = $"## {section.Trim()}";
        var allLines = currentContent.Split('\n').ToList();

        var sectionStart = allLines.FindIndex(l =>
            l.TrimEnd().Equals(sectionHeading, StringComparison.OrdinalIgnoreCase));

        if (sectionStart < 0)
        {
            // Section not found → append at end.
            var appended = currentContent.TrimEnd() + "\n\n" + sectionHeading + "\n" + trimmedNew + "\n";
            return appended;
        }

        // Find the end of this section (next ## heading or EOF).
        var sectionEnd = allLines.FindIndex(sectionStart + 1,
            l => l.TrimStart().StartsWith("## ", StringComparison.Ordinal));

        var before = allLines.Take(sectionStart + 1).ToList(); // include the ## heading line
        var after = sectionEnd >= 0 ? allLines.Skip(sectionEnd).ToList() : [];

        var result = string.Join("\n", before)
            + "\n"
            + trimmedNew
            + "\n"
            + (after.Count > 0 ? "\n" + string.Join("\n", after).TrimEnd() + "\n" : "");

        return result;
    }

    private static string GetDefaultContent(string target) => target.ToLowerInvariant() switch
    {
        "identity" => "# Koda Identity\n\n",
        "soul" => "# Koda Soul\n\n",
        "user" => "# User Profile\n\n",
        "memory" => "# Long-Term Memory\n\n",
        "agents" => "# KodaClaw Workspace Rules\n\n",
        "heartbeat" => "# Heartbeat Automations\n\n",
        _ => "# Workspace\n\n",
    };
}

/// <summary>
/// Arguments for the workspace_protocol_update tool.
/// </summary>
public sealed class WorkspaceProtocolUpdateArgs
{
    [ToolParameter(Description = "The protocol file to update. One of: identity, soul, user, memory, agents, heartbeat.")]
    public required string Target { get; init; }

    [ToolParameter(Description = "The ## section heading to update. If omitted, replaces everything after the # title line.", Required = false)]
    public string? Section { get; init; }

    [ToolParameter(Description = "The new content for the section. Agent decides the full markdown content.")]
    public required string Content { get; init; }
}
