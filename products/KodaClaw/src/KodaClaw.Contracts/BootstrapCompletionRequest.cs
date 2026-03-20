namespace KodaClaw.Contracts;

public sealed record BootstrapCompletionRequest(
    string IdentityMarkdown,
    string UserMarkdown,
    bool ArchiveBootstrapFile = true);
