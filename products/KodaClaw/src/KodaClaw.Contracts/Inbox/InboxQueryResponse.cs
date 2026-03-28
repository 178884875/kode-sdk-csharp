namespace KodaClaw.Contracts;

public sealed record InboxQueryResponse(
    IReadOnlyList<InboxItem> Items);
