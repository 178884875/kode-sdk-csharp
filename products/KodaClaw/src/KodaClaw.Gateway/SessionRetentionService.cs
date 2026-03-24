using System.Text.Json;
using KodaClaw.Contracts;
using Microsoft.Extensions.Logging;

namespace KodaClaw.Gateway;

/// <summary>
/// 清理 auto-* session 文件夹，按任务分组保留最近 N 次且不超过 D 天。
/// main-* 和 channel-* 不自动清理。
/// </summary>
internal sealed class SessionRetentionService
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ILogger<SessionRetentionService>? _logger;

    public SessionRetentionService(
        IWorkspaceService workspaceService,
        ILogger<SessionRetentionService>? logger = null)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var appConfig = await _workspaceService.LoadAppConfigAsync(cancellationToken);
        var retentionDays = appConfig.AutoSessionRetentionDays;
        var maxPerTask = appConfig.AutoSessionRetentionMaxPerTask;

        var sessionsRoot = Path.Combine(_workspaceService.RootPath, KodaClawWorkspaceLayout.SessionsDirectory);
        if (!Directory.Exists(sessionsRoot))
        {
            return;
        }

        var autoFolders = Directory.GetDirectories(sessionsRoot)
            .Where(d => Path.GetFileName(d).StartsWith("auto-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (autoFolders.Count == 0)
        {
            return;
        }

        // 只处理已完成的（有 meta.json 的），跳过正在执行中的
        var completed = autoFolders
            .Where(d => File.Exists(Path.Combine(d, "meta.json")))
            .ToList();

        _logger?.LogInformation(
            "SessionRetention: found {Total} auto- folders, {Completed} completed. retentionDays={Days}, maxPerTask={Max}",
            autoFolders.Count, completed.Count, retentionDays, maxPerTask);

        // 按任务 ID 分组（文件夹名格式：auto-{timestamp}-{taskId}-{suffix}）
        var groups = completed
            .GroupBy(d => ExtractTaskId(Path.GetFileName(d)))
            .ToList();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var deleted = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 按创建时间降序排列（时间戳在文件夹名中，可直接字典序排序）
            var sorted = group
                .OrderByDescending(d => Path.GetFileName(d))
                .ToList();

            for (var i = 0; i < sorted.Count; i++)
            {
                var dir = sorted[i];
                var folderName = Path.GetFileName(dir);

                // 保留规则：排名在 maxPerTask 内 且 创建时间在 cutoff 之后，两者都满足才保留
                var withinCount = i < maxPerTask;
                var createdAt = ParseCreatedAt(dir) ?? ExtractTimestampFromName(folderName);
                var withinDays = createdAt > cutoff;

                if (withinCount && withinDays)
                {
                    continue;
                }

                try
                {
                    Directory.Delete(dir, recursive: true);
                    deleted++;
                    _logger?.LogDebug("SessionRetention: deleted {Folder}", folderName);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "SessionRetention: failed to delete {Folder}", folderName);
                }
            }
        }

        _logger?.LogInformation("SessionRetention: deleted {Deleted} expired auto- session folders", deleted);
    }

    /// <summary>
    /// 从文件夹名 auto-{timestamp}-{taskId}-{suffix} 中提取 taskId。
    /// </summary>
    private static string ExtractTaskId(string folderName)
    {
        // 格式: auto-20260324143022-daily-check-a3f2b1c4
        // 跳过前缀 "auto-" 和时间戳段，剩余部分视为 taskId（最后 8 位随机后缀不参与分组）
        var parts = folderName.Split('-');
        if (parts.Length < 4)
        {
            return folderName;
        }

        // parts[0] = "auto", parts[1] = timestamp, parts[^1] = 8位随机后缀
        // 中间部分拼回来作为 taskId
        return string.Join("-", parts[2..^1]);
    }

    /// <summary>
    /// 从 meta.json 的 createdAt 字段读取创建时间（更精确）。
    /// </summary>
    private static DateTimeOffset? ParseCreatedAt(string sessionDir)
    {
        try
        {
            var metaPath = Path.Combine(sessionDir, "meta.json");
            var json = File.ReadAllText(metaPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("createdAt", out var createdAtEl)
                && createdAtEl.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(createdAtEl.GetString(), out var result))
            {
                return result;
            }
        }
        catch
        {
            // 解析失败时降级为文件夹名时间戳
        }

        return null;
    }

    /// <summary>
    /// 从文件夹名中解析时间戳作为兜底（格式：auto-yyyyMMddHHmmss-...）。
    /// </summary>
    private static DateTimeOffset ExtractTimestampFromName(string folderName)
    {
        try
        {
            var parts = folderName.Split('-');
            if (parts.Length >= 2
                && DateTimeOffset.TryParseExact(
                    parts[1], "yyyyMMddHHmmss",
                    null, System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var result))
            {
                return result;
            }
        }
        catch
        {
            // ignore
        }

        return DateTimeOffset.MinValue;
    }
}
