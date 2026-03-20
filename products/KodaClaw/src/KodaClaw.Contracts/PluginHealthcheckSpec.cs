namespace KodaClaw.Contracts;

public sealed record PluginHealthcheckSpec(
    string? ToolName = null,
    int? IntervalSeconds = null,
    int? TimeoutSeconds = null);
