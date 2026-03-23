namespace KodaClaw.McpHub;

public sealed record McpConnectionTestResult(
    bool Success,
    int ToolCount,
    string? ErrorMessage,
    IReadOnlyList<string>? ToolNames = null);
