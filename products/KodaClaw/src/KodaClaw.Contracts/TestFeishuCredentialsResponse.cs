namespace KodaClaw.Contracts;

public sealed record TestFeishuCredentialsResponse(
    bool Ok,
    string? AppName = null,
    string? Error = null);
