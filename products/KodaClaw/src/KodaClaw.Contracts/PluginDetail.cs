namespace KodaClaw.Contracts;

public sealed record PluginDetail(
    PluginRecord Record,
    PluginPermissionRiskSummary PermissionSummary,
    PluginHealthSummary HealthSummary,
    IReadOnlyList<string> AvailableTools);
