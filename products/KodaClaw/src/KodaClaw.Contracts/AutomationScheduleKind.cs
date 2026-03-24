using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AutomationScheduleKind>))]
public enum AutomationScheduleKind
{
    Hourly,
    Daily,
    Weekly,
    Minutes,
}
