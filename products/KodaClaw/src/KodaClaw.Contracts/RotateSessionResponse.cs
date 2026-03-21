namespace KodaClaw.Contracts;

public sealed record RotateSessionResponse(
    bool Ok,
    string? PreviousSessionId);
