namespace KodaClaw.Contracts;

public sealed record BootstrapStateResponse(
    string WorkspaceRootPath,
    int WorkspaceVersion,
    bool WorkspaceInitialized,
    bool RequiresBootstrap,
    string? ActiveMainSessionId,
    AppMode Mode);
