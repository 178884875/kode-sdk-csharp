using Microsoft.Extensions.Hosting;

namespace KodaClaw.Gateway;

internal sealed class StartupRepairHostedService : IHostedService
{
    private readonly WorkspaceRepairService _workspaceRepairService;

    public StartupRepairHostedService(WorkspaceRepairService workspaceRepairService)
    {
        _workspaceRepairService = workspaceRepairService ?? throw new ArgumentNullException(nameof(workspaceRepairService));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _workspaceRepairService.ExecuteStartupInspectionAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
