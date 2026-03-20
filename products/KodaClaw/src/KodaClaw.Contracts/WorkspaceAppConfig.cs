namespace KodaClaw.Contracts;

public sealed record WorkspaceAppConfig
{
    public int WorkspaceVersion { get; init; } = KodaClawWorkspaceLayout.CurrentWorkspaceVersion;

    public bool BootstrapCompleted { get; init; }

    public string? ActiveMainSessionId { get; init; }
}
