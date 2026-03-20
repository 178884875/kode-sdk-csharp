using KodaClaw.Contracts;

namespace KodaClaw.ControlPlane;

public sealed class InMemoryDiagnosticsService : IDiagnosticsService
{
    private const int MaxEvents = 200;
    private readonly object _gate = new();
    private readonly List<DiagnosticEvent> _events = [];

    public IReadOnlyList<DiagnosticEvent> GetRecent(int limit = 50, string? correlationId = null)
    {
        return Query(new DiagnosticsQuery(Limit: limit, CorrelationId: correlationId));
    }

    public IReadOnlyList<DiagnosticEvent> Query(DiagnosticsQuery? query = null)
    {
        var effective = query ?? new DiagnosticsQuery();
        if (effective.Limit <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            IEnumerable<DiagnosticEvent> filtered = _events;
            if (!string.IsNullOrWhiteSpace(effective.CorrelationId))
            {
                filtered = filtered.Where(item =>
                    string.Equals(item.CorrelationId, effective.CorrelationId, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(effective.SessionId))
            {
                filtered = filtered.Where(item =>
                    string.Equals(item.SessionId, effective.SessionId, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(effective.Source))
            {
                filtered = filtered.Where(item =>
                    string.Equals(item.Source, effective.Source, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(effective.EventType))
            {
                filtered = filtered.Where(item =>
                    string.Equals(item.EventType, effective.EventType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(effective.Level))
            {
                filtered = filtered.Where(item =>
                    string.Equals(item.Level, effective.Level, StringComparison.OrdinalIgnoreCase));
            }

            return filtered
                .OrderByDescending(item => item.Timestamp)
                .Take(effective.Limit)
                .ToArray();
        }
    }

    public void Record(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        lock (_gate)
        {
            _events.Add(diagnosticEvent);
            if (_events.Count > MaxEvents)
            {
                _events.RemoveRange(0, _events.Count - MaxEvents);
            }
        }
    }
}
