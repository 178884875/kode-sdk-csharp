using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Builtin.FileSystem;

/// <summary>
/// Tool for searching files with glob patterns.
/// </summary>
[Tool("fs_glob")]
[ToolAttributes(ReadOnly = true, NoEffect = true)]
public sealed class FsGlobTool : ToolBase<FsGlobArgs>
{
    public override string Name => "fs_glob";

    public override string Description =>
        "Find files matching a glob pattern. Returns a list of matching file paths. " +
        "Defaults to 200 results; use maxResults to adjust the limit.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<FsGlobArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = true,
        NoEffect = true
    };

    protected override async Task<ToolResult> ExecuteAsync(
        FsGlobArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await context.Sandbox.GlobAsync(args.Pattern, cancellationToken);

            var limit = args.MaxResults ?? 200;
            var fileList = files.Take(limit).ToList();

            return ToolResult.Ok(new
            {
                pattern = args.Pattern,
                files = fileList,
                count = fileList.Count,
                truncated = files.Count > limit,
                totalMatched = files.Count
            });
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to search files: {ex.Message}");
        }
    }
}

/// <summary>
/// Arguments for fs_glob tool.
/// </summary>
[GenerateToolSchema]
public class FsGlobArgs
{
    /// <summary>
    /// The glob pattern to match.
    /// </summary>
    [ToolParameter(Description = "Glob pattern to match files (e.g., **/*.cs, src/**/*.json)")]
    public required string Pattern { get; init; }

    /// <summary>
    /// Maximum number of results to return. Defaults to 500.
    /// </summary>
    [ToolParameter(Description = "Maximum number of files to return (default: 200)", Required = false)]
    public int? MaxResults { get; init; }
}
