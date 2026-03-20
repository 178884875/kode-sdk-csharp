using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AutomationDefinitionSource>))]
public enum AutomationDefinitionSource
{
    Heartbeat,
    Manual,
}
