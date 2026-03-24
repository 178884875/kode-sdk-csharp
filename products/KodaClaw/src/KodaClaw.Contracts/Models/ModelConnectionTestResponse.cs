namespace KodaClaw.Contracts;

public sealed record ModelConnectionTestResponse(
    bool Ok,
    int LatencyMs,
    string? ModelId,
    string? Error,
    string? ErrorMessage = null
);
