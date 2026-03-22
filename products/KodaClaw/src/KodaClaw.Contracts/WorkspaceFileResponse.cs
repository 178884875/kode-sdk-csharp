namespace KodaClaw.Contracts;

public sealed record WorkspaceFileResponse(
    string Target,
    string Content);

public sealed record WorkspaceFileUpdateRequest(
    string Content);
