using Microsoft.Extensions.Hosting;

namespace KodaClaw.Gateway;

internal sealed class StartupRepairHostedService : IHostedService
{
    private readonly WorkspaceRepairService _workspaceRepairService;
    private readonly IHostApplicationLifetime _lifetime;

    public StartupRepairHostedService(
        WorkspaceRepairService workspaceRepairService,
        IHostApplicationLifetime lifetime)
    {
        _workspaceRepairService = workspaceRepairService ?? throw new ArgumentNullException(nameof(workspaceRepairService));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Use ApplicationStopping (not the startup token) so the inspection is not
        // killed by the 30-second HostOptions.StartupTimeout when there are many
        // sessions to scan.  The inspection will still be cancelled on graceful shutdown.
        await _workspaceRepairService.ExecuteStartupInspectionAsync(_lifetime.ApplicationStopping);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
