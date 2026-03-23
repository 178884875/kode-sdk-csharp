namespace KodaClaw.McpHub;

public sealed record McpHubInjectionResult(
    int ServerCount,
    int ToolCount,
    int FailedServerCount,
    IReadOnlyList<string> FailedServers,
    IReadOnlyList<string> InjectedToolNames)
{
    public static readonly McpHubInjectionResult Empty =
        new(0, 0, 0, [], []);
}
