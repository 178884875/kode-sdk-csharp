namespace KodaClaw.Contracts;

public sealed record BootstrapCompletionRequest(
    string IdentityMarkdown,
    string SoulMarkdown,
    string UserMarkdown,
    bool ArchiveBootstrapFile = true);
