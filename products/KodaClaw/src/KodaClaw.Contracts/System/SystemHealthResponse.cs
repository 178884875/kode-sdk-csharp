namespace KodaClaw.Contracts;

public sealed record SystemHealthResponse(
    string Name,
    string Status,
    AppMode Mode);
