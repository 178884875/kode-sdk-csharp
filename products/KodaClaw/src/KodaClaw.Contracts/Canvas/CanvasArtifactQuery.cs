namespace KodaClaw.Contracts;

public sealed record CanvasArtifactQuery(
    CanvasArtifactKind? Kind = null,
    string? Source = null,
    string? SessionId = null,
    int Limit = 50);
