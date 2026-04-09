namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Controls how <c>MaxIterations</c> (and <c>MaxContextTokens</c>) are determined
/// for a sub-agent invocation.
/// </summary>
public enum MaxIterationsMode
{
    /// <summary>
    /// Use the explicit <c>MaxIterations</c> / <c>MaxContextTokens</c> values provided by the caller.
    /// This is the default — no complexity analysis is performed.
    /// </summary>
    Fixed,

    /// <summary>
    /// Before running the task, analyse its complexity with a lightweight sub-agent call
    /// (no tools, ~0.5 s) and automatically set <c>MaxIterations</c> and <c>MaxContextTokens</c>
    /// to match the estimated workload. Useful when the caller cannot predict task complexity.
    /// </summary>
    Auto,
}
