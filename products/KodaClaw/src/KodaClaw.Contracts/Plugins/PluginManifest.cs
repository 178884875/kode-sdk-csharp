using System.Text.Json;

namespace KodaClaw.Contracts;

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<PluginType> Types,
    PluginRuntimeSpec Runtime,
    PluginPermissionSet Permissions,
    PluginCapabilitySet Capabilities,
    PluginDisplaySpec? Display = null,
    PluginHealthcheckSpec? Healthcheck = null,
    JsonElement? ConfigSchema = null);
