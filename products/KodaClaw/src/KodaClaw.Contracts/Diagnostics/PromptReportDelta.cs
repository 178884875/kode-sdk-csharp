namespace KodaClaw.Contracts;

public sealed record PromptReportDelta(
    DateTimeOffset? PreviousGeneratedAt,
    int CharacterCountDelta,
    bool TruncationStateChanged,
    IReadOnlyList<string> AddedContextFiles,
    IReadOnlyList<string> RemovedContextFiles);
