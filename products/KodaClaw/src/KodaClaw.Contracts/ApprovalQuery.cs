namespace KodaClaw.Contracts;

public sealed record ApprovalQuery(
    ApprovalStatus? Status = null,
    ApprovalKind? Kind = null,
    string? SessionId = null,
    int Limit = 50);
