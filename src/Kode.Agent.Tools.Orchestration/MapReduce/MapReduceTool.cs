using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration.Internal;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Splits a list of items into chunks, processes each chunk in a parallel map
/// sub-agent, then passes all map results to a single reduce sub-agent for aggregation.
///
/// Use when you have a large homogeneous dataset (memory files, log entries, code modules)
/// that exceeds a single sub-agent's context but each piece can be processed independently.
/// </summary>
[Tool("map_reduce")]
[ToolAttributes(ReadOnly = true, NoEffect = true)]
public sealed class MapReduceTool : ToolBase<MapReduceArgs>
{
    private readonly IModelProvider _modelProvider;
    private readonly string _modelId;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    public MapReduceTool(
        IModelProvider modelProvider,
        string modelId,
        IToolRegistry toolRegistry,
        ISandboxFactory sandboxFactory,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        _modelProvider = modelProvider;
        _modelId = modelId;
        _toolRegistry = toolRegistry;
        _sandboxFactory = sandboxFactory;
        _loggerFactory = loggerFactory;
    }

    public override string Name => "map_reduce";

    public override string Description =>
        "Process a large list of items by splitting into chunks, processing each chunk in parallel " +
        "map sub-agents, then aggregating all results with a single reduce sub-agent. " +
        "Use this when a dataset is too large for a single sub-agent's context " +
        "(e.g. 100+ memory files, large log sets, many code modules). " +
        "The MapTask template uses {item} as a placeholder for the chunk content.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<MapReduceArgs>();

    public override ToolAttributes Attributes => new() { ReadOnly = true, NoEffect = true };

    public override ValueTask<string?> GetPromptAsync(ToolContext context) =>
        ValueTask.FromResult<string?>(
            "Use map_reduce for large homogeneous datasets where each piece can be processed independently. " +
            "Write MapTask with {item} placeholder. Write ReduceTask to aggregate the map summaries. " +
            "Set ChunkSize > 1 to batch multiple items per sub-agent.");

    protected override async Task<ToolResult> ExecuteAsync(
        MapReduceArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
            return ToolResult.Fail("No model ID configured for map_reduce sub-agents.");

        if (args.Items is not { Count: > 0 })
            return ToolResult.Fail("Items must not be empty.");

        if (string.IsNullOrWhiteSpace(args.MapTask))
            return ToolResult.Fail("MapTask must not be empty.");

        if (string.IsNullOrWhiteSpace(args.ReduceTask))
            return ToolResult.Fail("ReduceTask must not be empty.");

        var chunkSize = Math.Max(1, args.ChunkSize);
        var chunks = args.Items
            .Select((item, i) => (item, i))
            .GroupBy(x => x.i / chunkSize)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();

        // ── map phase ─────────────────────────────────────────────────────
        SemaphoreSlim? semaphore = args.MaxConcurrency > 0
            ? new SemaphoreSlim(args.MaxConcurrency, args.MaxConcurrency)
            : null;

        try
        {
            var mapResults = new SubAgentResult[chunks.Count];
            var mapTasks = chunks.Select((chunk, i) =>
                RunMapAsync(chunk, i, args, context, mapResults, semaphore, cancellationToken)
            ).ToArray();
            await Task.WhenAll(mapTasks);

            // ── reduce phase ──────────────────────────────────────────────
            var reduceTask = BuildReduceTask(mapResults, args.ReduceTask);
            var reduceResult = await SubAgentRunner.RunAsync(new SubAgentRequest
            {
                Task = reduceTask,
                Tools = null,        // reduce is typically reasoning-only; use defaults
                MaxIterations = args.ReduceMaxIterations,
                ParentSandboxOptions = context.SandboxOptions,
                ModelProvider = _modelProvider,
                ModelId = _modelId,
                ToolRegistry = _toolRegistry,
                SandboxFactory = _sandboxFactory,
                LoggerFactory = _loggerFactory,
            }, cancellationToken);

            var mapSummary = mapResults.Select((r, i) => new
            {
                chunk = i,
                items = chunks[i],
                success = r.Success,
                summary = r.Summary,
                error = r.Error,
            }).ToList();

            return ToolResult.Ok(new
            {
                reduction = reduceResult.Success ? reduceResult.Summary : null,
                reductionError = reduceResult.Success ? null : reduceResult.Error,
                totalItems = args.Items.Count,
                totalChunks = chunks.Count,
                mapResults = mapSummary,
                succeeded = reduceResult.Success,
            });
        }
        finally
        {
            semaphore?.Dispose();
        }
    }

    private async Task RunMapAsync(
        List<string> chunk, int index, MapReduceArgs args, ToolContext context,
        SubAgentResult[] results, SemaphoreSlim? semaphore, CancellationToken ct)
    {
        if (semaphore is not null) await semaphore.WaitAsync(ct);
        try
        {
            var itemContent = chunk.Count == 1
                ? chunk[0]
                : string.Join("\n---\n", chunk.Select((item, i) => $"Item {i + 1}:\n{item}"));

            var task = args.MapTask.Replace("{item}", itemContent, StringComparison.OrdinalIgnoreCase);

            results[index] = await SubAgentRunner.RunAsync(new SubAgentRequest
            {
                Task = task,
                Tools = args.MapTools,
                MaxIterations = args.MapMaxIterations,
                ParentSandboxOptions = context.SandboxOptions,
                ModelProvider = _modelProvider,
                ModelId = _modelId,
                ToolRegistry = _toolRegistry,
                SandboxFactory = _sandboxFactory,
                LoggerFactory = _loggerFactory,
            }, ct);
        }
        catch (OperationCanceledException)
        {
            results[index] = SubAgentResult.Fail("Cancelled");
        }
        finally
        {
            semaphore?.Release();
        }
    }

    private static string BuildReduceTask(SubAgentResult[] mapResults, string reduceTask)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Map phase results:");
        sb.AppendLine();

        for (var i = 0; i < mapResults.Length; i++)
        {
            sb.AppendLine($"## Chunk {i + 1}");
            sb.AppendLine(mapResults[i].Success
                ? mapResults[i].Summary ?? "(no output)"
                : $"(failed: {mapResults[i].Error})");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine(reduceTask);
        return sb.ToString();
    }
}
