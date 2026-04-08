namespace KodaClaw.Contracts;

public sealed record DomSnapshot(
    string Url,
    string Title,
    IReadOnlyList<DomElement> Elements,
    string? RawMarkdown = null);
