namespace KodaClaw.Contracts;

public sealed record CookieInfo(
    string Name,
    string Domain,
    string? Value = null,
    bool IsHttpOnly = false,
    bool IsSecure = false,
    DateTimeOffset? Expires = null);
