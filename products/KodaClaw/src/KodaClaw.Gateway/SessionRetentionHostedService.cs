using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KodaClaw.Gateway;

/// <summary>
/// 启动时执行一次 session 保留清理，之后每天凌晨 3 点执行一次。
/// </summary>
internal sealed class SessionRetentionHostedService : BackgroundService
{
    private readonly SessionRetentionService _retentionService;
    private readonly ILogger<SessionRetentionHostedService>? _logger;

    public SessionRetentionHostedService(
        SessionRetentionService retentionService,
        ILogger<SessionRetentionHostedService>? logger = null)
    {
        _retentionService = retentionService ?? throw new ArgumentNullException(nameof(retentionService));
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时立即执行一次
        await RunRetentionAsync(stoppingToken);

        // 之后每天在凌晨 3 点执行
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun();
            _logger?.LogDebug("SessionRetention: next run in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunRetentionAsync(stoppingToken);
        }
    }

    private async Task RunRetentionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _retentionService.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 正常关闭，不记录错误
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SessionRetention: unhandled error during retention run");
        }
    }

    private static TimeSpan ComputeDelayUntilNextRun()
    {
        var now = DateTimeOffset.Now;
        var nextRun = now.Date.AddDays(1).AddHours(3); // 明天凌晨 3 点（本地时间）
        var delay = nextRun - now;

        // 防止极端情况下 delay 为负（例如当前正好是 03:00:00）
        if (delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromHours(24);
        }

        return delay;
    }
}
