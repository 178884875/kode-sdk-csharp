using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KodaClaw.Contracts;

namespace KodaClaw.ControlPlane;

public sealed class InMemoryDiagnosticsService : IDiagnosticsService, IDisposable
{
    private const int MaxEvents = 500;
    private readonly object _gate = new();
    private readonly List<DiagnosticEvent> _events = [];
    private readonly List<Channel<DiagnosticEvent>> _subscribers = [];

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

            if (effective.Levels is { Length: > 0 })
            {
                filtered = filtered.Where(item =>
                    effective.Levels.Any(l =>
                        string.Equals(item.Level, l, StringComparison.OrdinalIgnoreCase)));
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

            foreach (var ch in _subscribers)
            {
                ch.Writer.TryWrite(diagnosticEvent);
            }
        }
    }

    public DiagnosticsStatsResponse GetStats(DateTimeOffset? since = null)
    {
        lock (_gate)
        {
            var source = since.HasValue
                ? _events.Where(e => e.Timestamp >= since.Value).ToArray()
                : (IReadOnlyList<DiagnosticEvent>)_events;

            if (source.Count == 0)
            {
                return new DiagnosticsStatsResponse(0, 0, 0, [], null, null);
            }

            var bySource = source
                .GroupBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DiagnosticsSourceStats(
                    Source: g.Key,
                    Count: g.Count(),
                    ErrorCount: g.Count(e => string.Equals(e.Level, "error", StringComparison.OrdinalIgnoreCase)),
                    WarningCount: g.Count(e => string.Equals(e.Level, "warning", StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(s => s.Count)
                .ToArray();

            return new DiagnosticsStatsResponse(
                TotalEvents: source.Count,
                ErrorCount: source.Count(e => string.Equals(e.Level, "error", StringComparison.OrdinalIgnoreCase)),
                WarningCount: source.Count(e => string.Equals(e.Level, "warning", StringComparison.OrdinalIgnoreCase)),
                BySource: bySource,
                OldestEvent: source.Min(e => e.Timestamp),
                NewestEvent: source.Max(e => e.Timestamp));
        }
    }

    public Task ClearAsync(DateTimeOffset? before = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (before.HasValue)
            {
                _events.RemoveAll(e => e.Timestamp < before.Value);
            }
            else
            {
                _events.Clear();
            }
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DiagnosticEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<DiagnosticEvent>(
            new UnboundedChannelOptions { SingleReader = true });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var ch in _subscribers)
            {
                ch.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }
}
