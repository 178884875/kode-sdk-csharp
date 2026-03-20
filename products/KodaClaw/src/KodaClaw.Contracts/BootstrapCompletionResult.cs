namespace KodaClaw.Contracts;

public sealed record BootstrapCompletionResult(
    string WorkspaceRootPath,
    bool BootstrapCompleted,
    string IdentityFilePath,
    string UserFilePath,
    bool BootstrapFileArchived);
