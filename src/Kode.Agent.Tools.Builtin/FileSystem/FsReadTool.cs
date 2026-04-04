using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Builtin.FileSystem;

/// <summary>
/// Tool for reading file contents.
/// </summary>
[Tool("fs_read")]
[ToolAttributes(ReadOnly = true, NoEffect = true)]
public sealed class FsReadTool : ToolBase<FsReadArgs>
{
    public override string Name => "fs_read";

    public override string Description =>
        "Read the contents of a file. Returns the file content as text. " +
        "Supports optional line range selection. " +
        "Files exceeding 500 lines are truncated by default; use startLine/endLine to read specific sections.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<FsReadArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = true,
        NoEffect = true
    };

    public override ValueTask<string?> GetPromptAsync(ToolContext context)
    {
        return ValueTask.FromResult<string?>(
            "File reading strategy:\n" +
            "1. If you only need to find a keyword, function, or pattern — use fs_grep instead. " +
            "It returns only matching lines and is far more context-efficient.\n" +
            "2. If you need to read a file, always scope it: use startLine/endLine to read " +
            "only the section you need. Avoid reading the whole file unless necessary.\n" +
            "3. Files over 500 lines are automatically truncated. If truncated=true in the " +
            "response, use startLine/endLine to read subsequent sections.");
    }

    protected override async Task<ToolResult> ExecuteAsync(
        FsReadArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await context.Sandbox.FileExistsAsync(args.Path, cancellationToken))
            {
                return ToolResult.Fail($"File not found: {args.Path}");
            }

            var content = await context.Sandbox.ReadFileAsync(args.Path, cancellationToken);

            var allLines = content.Split('\n');
            var totalLines = allLines.Length;

            // Apply line range if specified, otherwise enforce an adaptive line cap.
            // When context pressure is high the cap shrinks to preserve headroom.
            bool truncated;
            int start, end;
            if (args.StartLine.HasValue || args.EndLine.HasValue)
            {
                start = Math.Max(0, (args.StartLine ?? 1) - 1);
                end = Math.Min(totalLines, args.EndLine ?? totalLines);
                truncated = false;
            }
            else
            {
                var maxLines = context.ContextPressure switch
                {
                    >= 0.8f => 100,
                    >= 0.6f => 200,
                    >= 0.4f => 350,
                    _       => 500
                };
                start = 0;
                end = Math.Min(totalLines, maxLines);
                truncated = totalLines > maxLines;
            }

            content = string.Join('\n', allLines.Skip(start).Take(end - start));

            var filePool = ToolContextFilePool.TryGetFilePool(context);
            if (filePool != null)
            {
                await filePool.RecordReadAsync(args.Path, cancellationToken);
            }

            return ToolResult.Ok(new
            {
                path = args.Path,
                content,
                lines = end - start,
                totalLines,
                truncated,
                note = truncated
                    ? $"File has {totalLines} lines; showing lines 1-{end} (limit={end} due to context pressure {context.ContextPressure:P0}). Use startLine/endLine to read other sections."
                    : (string?)null
            });
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to read file: {ex.Message}");
        }
    }
}

/// <summary>
/// Arguments for fs_read tool.
/// </summary>
[GenerateToolSchema]
public class FsReadArgs
{
    /// <summary>
    /// The path to the file to read.
    /// </summary>
    [ToolParameter(Description = "The absolute or relative path to the file to read")]
    public required string Path { get; init; }

    /// <summary>
    /// Optional starting line number (1-based).
    /// </summary>
    [ToolParameter(Description = "The line number to start reading from (1-based)", Required = false)]
    public int? StartLine { get; init; }

    /// <summary>
    /// Optional ending line number (1-based, inclusive).
    /// </summary>
    [ToolParameter(Description = "The line number to stop reading at (1-based, inclusive)", Required = false)]
    public int? EndLine { get; init; }
}
