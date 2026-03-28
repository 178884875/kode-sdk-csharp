namespace KodaClaw.Contracts;

public sealed record BootstrapDraftResult(
    string IdentityMarkdown,
    string SoulMarkdown,
    string UserMarkdown,
    string Summary);
