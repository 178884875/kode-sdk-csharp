using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.McpHub;

public interface IMcpHubService
{
    /// <summary>
    /// Reads workspace/mcp.json, connects to each enabled MCP server, and injects
    /// their tools into the provided <paramref name="toolRegistry"/>.
    /// Single-server failures are isolated; other servers continue normally.
    /// </summary>
    Task<McpHubInjectionResult> InjectToolsAsync(
        string sessionId,
        IToolRegistry toolRegistry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to connect to the named MCP server and call ListTools.
    /// Returns success/failure result without modifying the tool registry.
    /// </summary>
    Task<McpConnectionTestResult> TestConnectionAsync(
        string serverName,
        CancellationToken cancellationToken = default);
}
