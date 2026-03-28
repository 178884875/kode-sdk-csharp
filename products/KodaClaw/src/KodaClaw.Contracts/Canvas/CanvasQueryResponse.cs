namespace KodaClaw.Contracts;

public sealed record CanvasQueryResponse(
    IReadOnlyList<CanvasArtifact> Items,
    string DefaultEntryPath,
    string? DefaultArtifactId = null);
