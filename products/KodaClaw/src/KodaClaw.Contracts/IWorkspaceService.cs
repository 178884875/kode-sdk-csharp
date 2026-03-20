namespace KodaClaw.Contracts;

public interface IWorkspaceService
{
    string RootPath { get; }

    Task<WorkspaceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<WorkspaceSnapshot> EnsureInitializedAsync(CancellationToken cancellationToken = default);

    Task<WorkspaceAppConfig> LoadAppConfigAsync(CancellationToken cancellationToken = default);

    Task SaveAppConfigAsync(WorkspaceAppConfig appConfig, CancellationToken cancellationToken = default);

    string GetSessionDirectory(string sessionId);
}
