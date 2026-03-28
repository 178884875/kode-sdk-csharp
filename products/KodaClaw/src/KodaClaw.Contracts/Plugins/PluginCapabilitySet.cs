namespace KodaClaw.Contracts;

public sealed record PluginCapabilitySet(
    IReadOnlyList<string>? Tools = null,
    IReadOnlyList<string>? Channels = null,
    IReadOnlyList<string>? UiPanels = null,
    IReadOnlyList<string>? MemoryProviders = null);
