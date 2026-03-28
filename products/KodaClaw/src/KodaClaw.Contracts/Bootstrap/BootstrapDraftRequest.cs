namespace KodaClaw.Contracts;

public sealed record BootstrapDraftRequest(
    IReadOnlyList<BootstrapDraftMessage>? Conversation,
    string? IdentityMarkdown = null,
    string? SoulMarkdown = null,
    string? UserMarkdown = null);
