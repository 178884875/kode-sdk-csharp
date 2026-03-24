using KodaClaw.Contracts;
using Microsoft.Extensions.Logging;

namespace KodaClaw.Workspace;

public interface IHeartbeatSyncService
{
    Task<HeartbeatSyncResult> SyncAsync(CancellationToken cancellationToken = default);
}

public sealed record HeartbeatSyncResult(int Upserted, int Deleted, bool CompilationFailed);

public sealed class HeartbeatSyncService : IHeartbeatSyncService
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IHeartbeatAutomationCompiler _compiler;
    private readonly IAutomationDefinitionRepository _definitionRepository;
    private readonly ILogger<HeartbeatSyncService>? _logger;

    public HeartbeatSyncService(
        IWorkspaceService workspaceService,
        IHeartbeatAutomationCompiler compiler,
        IAutomationDefinitionRepository definitionRepository,
        ILogger<HeartbeatSyncService>? logger = null)
    {
        _workspaceService = workspaceService;
        _compiler = compiler;
        _definitionRepository = definitionRepository;
        _logger = logger;
    }

    public async Task<HeartbeatSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        // Ensure workspace is initialized so that default files (including HEARTBEAT.md) are
        // written before we try to read them. This is idempotent — a no-op if already done.
        await _workspaceService.EnsureInitializedAsync(cancellationToken);

        var heartbeatPath = Path.Combine(
            _workspaceService.RootPath,
            KodaClawWorkspaceLayout.WorkspaceDirectory,
            KodaClawWorkspaceLayout.HeartbeatFile);

        if (!File.Exists(heartbeatPath))
        {
            _logger?.LogDebug("HEARTBEAT.md not found at {Path}, skipping sync.", heartbeatPath);
            return new HeartbeatSyncResult(Upserted: 0, Deleted: 0, CompilationFailed: false);
        }

        var markdown = await File.ReadAllTextAsync(heartbeatPath, cancellationToken);

        IReadOnlyList<AutomationDefinition> compiled;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            // 文件存在但内容为空，视为用户主动清空所有 automation（而非编译失败）
            compiled = Array.Empty<AutomationDefinition>();
        }
        else
        {
            try
            {
                compiled = _compiler.Compile(markdown);
            }
            catch (HeartbeatCompilationException ex)
            {
                _logger?.LogWarning(ex, "HEARTBEAT.md compilation failed: {Message}. Existing definitions are preserved.", ex.Message);
                return new HeartbeatSyncResult(Upserted: 0, Deleted: 0, CompilationFailed: true);
            }
        }

        var existing = await _definitionRepository.ListAsync(
            new AutomationDefinitionQuery(Source: AutomationDefinitionSource.Heartbeat, Limit: 1000),
            cancellationToken);

        var existingById = existing.ToDictionary(d => d.Id);
        var compiledIds = compiled.Select(d => d.Id).ToHashSet();

        var upserted = 0;
        foreach (var definition in compiled)
        {
            var merged = existingById.TryGetValue(definition.Id, out var existingDef)
                ? definition with
                  {
                      CreatedAt = existingDef.CreatedAt,
                      UpdatedAt = DateTimeOffset.UtcNow,
                      LastRunAt = existingDef.LastRunAt,
                      NextRunAt = existingDef.NextRunAt,
                      LastRunStatus = existingDef.LastRunStatus,
                      LastError = existingDef.LastError,
                  }
                : definition with { CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

            await _definitionRepository.UpsertAsync(merged, cancellationToken);
            upserted++;
        }

        var deleted = 0;
        foreach (var existingDef in existing)
        {
            if (!compiledIds.Contains(existingDef.Id))
            {
                await _definitionRepository.DeleteAsync(existingDef.Id, cancellationToken);
                deleted++;
            }
        }

        _logger?.LogInformation(
            "HEARTBEAT.md sync complete: {Upserted} upserted, {Deleted} deleted.",
            upserted, deleted);

        return new HeartbeatSyncResult(Upserted: upserted, Deleted: deleted, CompilationFailed: false);
    }
}
