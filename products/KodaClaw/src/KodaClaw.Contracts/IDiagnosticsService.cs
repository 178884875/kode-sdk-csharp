namespace KodaClaw.Contracts;

public interface IDiagnosticsService
{
    void Record(DiagnosticEvent diagnosticEvent);

    IReadOnlyList<DiagnosticEvent> GetRecent(int limit = 50, string? correlationId = null);

    IReadOnlyList<DiagnosticEvent> Query(DiagnosticsQuery? query = null);
}
