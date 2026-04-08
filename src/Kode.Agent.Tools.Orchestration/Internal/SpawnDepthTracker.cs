namespace Kode.Agent.Tools.Orchestration.Internal;

/// <summary>
/// Tracks sub-agent spawn depth within an async execution flow.
/// Uses AsyncLocal so depth is properly inherited by child tasks but not
/// leaked to sibling or parent async contexts.
/// </summary>
internal static class SpawnDepthTracker
{
    private static readonly AsyncLocal<int> _depth = new();

    /// <summary>
    /// Current spawn depth in this async execution context (0 = top-level caller).
    /// </summary>
    public static int Current => _depth.Value;

    /// <summary>
    /// Enters a new spawn depth level. Dispose the returned handle to exit.
    /// </summary>
    public static IDisposable Enter()
    {
        _depth.Value++;
        return new DepthScope();
    }

    private sealed class DepthScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _depth.Value--;
        }
    }
}
