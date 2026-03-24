namespace KodaClaw.Contracts;

public sealed record WorkspaceGitCommit(
    string Hash,
    string ShortHash,
    string Message,
    string Author,
    DateTimeOffset CommittedAt,
    IReadOnlyList<string> ChangedFiles);

public sealed record WorkspaceGitLogResponse(
    IReadOnlyList<WorkspaceGitCommit> Commits);

public sealed record WorkspaceGitRevertFileRequest(
    string Hash,
    string FilePath);

public sealed record WorkspaceGitRevertFileResponse(
    string NewHash);
