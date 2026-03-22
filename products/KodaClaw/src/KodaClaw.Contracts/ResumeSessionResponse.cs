namespace KodaClaw.Contracts;

public sealed record ResumeSessionResponse(
    bool Ok,
    string ResumedSessionId);
