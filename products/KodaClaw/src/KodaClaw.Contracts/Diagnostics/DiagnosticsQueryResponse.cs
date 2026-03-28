namespace KodaClaw.Contracts;

public sealed record DiagnosticsQueryResponse(
    IReadOnlyList<DiagnosticEvent> Events);
