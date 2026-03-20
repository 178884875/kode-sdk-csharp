using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AutomationScheduleDay>))]
public enum AutomationScheduleDay
{
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
    Sunday,
}
