namespace KodaClaw.Contracts;

public sealed record InboxQuery(
    InboxItemStatus? Status = null,
    InboxItemKind? Kind = null,
    bool? RequiresAction = null,
    string? SessionId = null,
    int Limit = 50);
