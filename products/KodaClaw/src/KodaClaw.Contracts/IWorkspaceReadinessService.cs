namespace KodaClaw.Contracts;

public interface IWorkspaceReadinessService
{
    Task<WorkspaceReadinessResponse> GetReadinessAsync(CancellationToken cancellationToken = default);
}
