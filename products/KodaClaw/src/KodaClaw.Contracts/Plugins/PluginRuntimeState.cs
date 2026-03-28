using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<PluginRuntimeState>))]
public enum PluginRuntimeState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Degraded = 3,
}
