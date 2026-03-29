using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using KodaClaw.Contracts;

namespace KodaClaw.ControlPlane;

public sealed class FileDiagnosticsService : IDiagnosticsService, IDisposable
{
    private const int HotCacheCapacity = 500;
    private const int ReloadLimit = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _journalPath;
    private readonly object _gate = new();
    private readonly List<DiagnosticEvent> _cache = [];
    private readonly List<Channel<DiagnosticEvent>> _subscribers = [];

    public FileDiagnosticsService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _journalPath = Path.Combine(workspaceRoot, KodaClawWorkspaceLayout.DiagnosticsJournalFile);
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
        LoadFromFile();
    }

    public void Record(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        lock (_gate)
        {
            _cache.Add(diagnosticEvent);
            if (_cache.Count > HotCacheCapacity)
            {
                _cache.RemoveRange(0, _cache.Count - HotCacheCapacity);
            }

            AppendToFile(diagnosticEvent);

            foreach (var ch in _subscribers)
            {
                ch.Writer.TryWrite(diagnosticEvent);
            }
        }
    }

    public IReadOnlyList<DiagnosticEvent> GetRecent(int limit = 50, string? correlationId = null)
    {
        return Query(new DiagnosticsQuery(Limit: limit, CorrelationId: correlationId));
    }

    public IReadOnlyList<DiagnosticEvent> Query(DiagnosticsQuery? query = null)
    {
        var q = query ?? new DiagnosticsQuery();
        if (q.Limit <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            IEnumerable<DiagnosticEvent> filtered = _cache;

            if (!string.IsNullOrWhiteSpace(q.CorrelationId))
            {
                filtered = filtered.Where(e => string.Equals(e.CorrelationId, q.CorrelationId, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(q.SessionId))
            {
                filtered = filtered.Where(e => string.Equals(e.SessionId, q.SessionId, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(q.Source))
            {
                filtered = filtered.Where(e => string.Equals(e.Source, q.Source, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(q.EventType))
            {
                filtered = filtered.Where(e => string.Equals(e.EventType, q.EventType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(q.Level))
            {
                filtered = filtered.Where(e => string.Equals(e.Level, q.Level, StringComparison.OrdinalIgnoreCase));
            }

            if (q.DateFrom.HasValue)
            {
                filtered = filtered.Where(e => e.Timestamp >= q.DateFrom.Value);
            }

            if (q.DateTo.HasValue)
            {
                filtered = filtered.Where(e => e.Timestamp <= q.DateTo.Value);
            }

            return filtered
                .OrderByDescending(e => e.Timestamp)
                .Take(q.Limit)
                .ToArray();
        }
    }

    public DiagnosticsStatsResponse GetStats(DateTimeOffset? since = null)
    {
        lock (_gate)
        {
            var source = since.HasValue
                ? _cache.Where(e => e.Timestamp >= since.Value).ToArray()
                : (IReadOnlyList<DiagnosticEvent>)_cache;

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

    public async Task ClearAsync(DateTimeOffset? before = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (before.HasValue)
            {
                _cache.RemoveAll(e => e.Timestamp < before.Value);
            }
            else
            {
                _cache.Clear();
            }
        }

        await RewriteFileAsync(before, cancellationToken);
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

    private void LoadFromFile()
    {
        if (!File.Exists(_journalPath))
        {
            return;
        }

        try
        {
            var lines = File.ReadLines(_journalPath)
                .Where(static l => !string.IsNullOrWhiteSpace(l))
                .TakeLast(ReloadLimit)
                .ToArray();

            foreach (var line in lines)
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<DiagnosticEvent>(line, JsonOptions);
                    if (evt is not null)
                    {
                        _cache.Add(evt);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileDiagnosticsService] Skipping corrupted journal line: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileDiagnosticsService] Failed to load journal file: {ex.Message}");
        }
    }

    private void AppendToFile(DiagnosticEvent diagnosticEvent)
    {
        try
        {
            var line = JsonSerializer.Serialize(diagnosticEvent, JsonOptions);
            File.AppendAllText(_journalPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileDiagnosticsService] Failed to write journal file: {ex.Message}");
        }
    }

    private async Task RewriteFileAsync(DateTimeOffset? keepFrom, CancellationToken cancellationToken)
    {
        if (!File.Exists(_journalPath))
        {
            return;
        }

        if (!keepFrom.HasValue)
        {
            try
            {
                File.Delete(_journalPath);
            }
            catch
            {
                // ignore
            }

            return;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(_journalPath, cancellationToken);
            var kept = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Where(l =>
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<DiagnosticEvent>(l, JsonOptions);
                        return evt is not null && evt.Timestamp >= keepFrom.Value;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();

            await File.WriteAllLinesAsync(_journalPath, kept, cancellationToken);
        }
        catch
        {
            // 重写失败不抛出，文件保持原样
        }
    }
}
