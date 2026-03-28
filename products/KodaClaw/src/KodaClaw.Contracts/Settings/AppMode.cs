using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AppMode>))]
public enum AppMode
{
    Bootstrap = 0,
    Normal = 1,
}
