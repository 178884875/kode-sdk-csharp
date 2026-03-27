using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Builtin.FileSystem;

/// <summary>
/// Tool for performing multiple file edits in a single operation.
/// </summary>
[Tool("fs_multi_edit")]
public sealed class FsMultiEditTool : ToolBase<FsMultiEditArgs>
{
    public override string Name => "fs_multi_edit";

    public override string Description =>
        "Perform multiple file edit operations in a single call. " +
        "Each edit specifies a file path, text to find, and replacement text. " +
        "More efficient than calling fs_edit multiple times.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<FsMultiEditArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        RequiresApproval = true
    };

    public override ValueTask<string?> GetPromptAsync(ToolContext context)
    {
        return ValueTask.FromResult<string?>(
            "Use fs_multi_edit for batch file modifications. Each edit operation specifies:\n" +
            "- path: file to edit\n" +
            "- find: text to replace\n" +
            "- replace: replacement text\n" +
            "- replaceAll: whether to replace all occurrences (default: false)\n\n" +
            "If a pattern occurs multiple times and replaceAll is false, the edit is skipped.");
    }

    protected override async Task<ToolResult> ExecuteAsync(
        FsMultiEditArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var filePool = ToolContextFilePool.TryGetFilePool(context);
        var results = new List<EditResultInfo>();
        var successCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        foreach (var edit in args.Edits)
        {
            if (string.IsNullOrWhiteSpace(edit.Path) || string.IsNullOrWhiteSpace(edit.Find))
            {
                results.Add(new EditResultInfo { Path = edit.Path, Replacements = 0, Status = "error", Message = "Each edit must have non-empty 'path' and 'find' fields." });
                errorCount++;
                continue;
            }

            try
            {
                var content = await context.Sandbox.ReadFileAsync(edit.Path, cancellationToken);

                if (edit.ReplaceAll)
                {
                    var occurrences = CountOccurrences(content, edit.Find);
                    if (occurrences == 0)
                    {
                        results.Add(new EditResultInfo
                        {
                            Path = edit.Path,
                            Replacements = 0,
                            Status = "skipped",
                            Message = "Pattern not found"
                        });
                        skippedCount++;
                        continue;
                    }

                    var updated = content.Replace(edit.Find, edit.Replace);
                    await context.Sandbox.WriteFileAsync(edit.Path, updated, cancellationToken);
                    if (filePool != null)
                    {
                        await filePool.RecordEditAsync(edit.Path, cancellationToken);
                    }

                    results.Add(new EditResultInfo
                    {
                        Path = edit.Path,
                        Replacements = occurrences,
                        Status = "ok"
                    });
                    successCount++;
                }
                else
                {
                    var index = content.IndexOf(edit.Find, StringComparison.Ordinal);
                    if (index == -1)
                    {
                        results.Add(new EditResultInfo
                        {
                            Path = edit.Path,
                            Replacements = 0,
                            Status = "skipped",
                            Message = "Pattern not found"
                        });
                        skippedCount++;
                        continue;
                    }

                    var occurrences = CountOccurrences(content, edit.Find);
                    if (occurrences > 1)
                    {
                        results.Add(new EditResultInfo
                        {
                            Path = edit.Path,
                            Replacements = 0,
                            Status = "skipped",
                            Message = $"Pattern occurs {occurrences} times; set replaceAll=true if intended"
                        });
                        skippedCount++;
                        continue;
                    }

                    var updated = ReplaceFirst(content, edit.Find, edit.Replace);
                    await context.Sandbox.WriteFileAsync(edit.Path, updated, cancellationToken);
                    if (filePool != null)
                    {
                        await filePool.RecordEditAsync(edit.Path, cancellationToken);
                    }

                    results.Add(new EditResultInfo
                    {
                        Path = edit.Path,
                        Replacements = 1,
                        Status = "ok"
                    });
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                results.Add(new EditResultInfo
                {
                    Path = edit.Path,
                    Replacements = 0,
                    Status = "error",
                    Message = ex.Message
                });
                errorCount++;
            }
        }

        return ToolResult.Ok(new
        {
            results,
            summary = new
            {
                total = args.Edits.Count,
                success = successCount,
                skipped = skippedCount,
                errors = errorCount
            }
        });
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string text, string find, string replace)
    {
        var index = text.IndexOf(find, StringComparison.Ordinal);
        if (index < 0) return text;
        return string.Concat(text.AsSpan(0, index), replace, text.AsSpan(index + find.Length));
    }
}

/// <summary>
/// Arguments for fs_multi_edit tool.
/// </summary>
[GenerateToolSchema]
public class FsMultiEditArgs
{
    /// <summary>
    /// List of edit operations to perform.
    /// </summary>
    [ToolParameter(Description = "List of edit operations")]
    public required IReadOnlyList<EditOperation> Edits { get; init; }
}

/// <summary>
/// Single edit operation within a multi-edit.
/// </summary>
public class EditOperation
{
    /// <summary>
    /// File path to edit.
    /// </summary>
    [ToolParameter(Description = "File path to edit")]
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Text to find and replace.
    /// </summary>
    [ToolParameter(Description = "Existing text to replace")]
    public string Find { get; init; } = string.Empty;

    /// <summary>
    /// Replacement text.
    /// </summary>
    [ToolParameter(Description = "Replacement text")]
    public string Replace { get; init; } = string.Empty;

    /// <summary>
    /// Whether to replace all occurrences.
    /// </summary>
    [ToolParameter(Description = "Replace all occurrences (default: false)", Required = false)]
    public bool ReplaceAll { get; init; }
}

/// <summary>
/// Result info for a single edit operation.
/// </summary>
internal class EditResultInfo
{
    public required string Path { get; init; }
    public required int Replacements { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
}
