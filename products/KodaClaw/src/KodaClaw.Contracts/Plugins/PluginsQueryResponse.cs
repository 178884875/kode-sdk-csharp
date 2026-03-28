namespace KodaClaw.Contracts;

public sealed record PluginsQueryResponse(
    IReadOnlyList<PluginSummary> Items);
