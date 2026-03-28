using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AutomationRunStatus>))]
public enum AutomationRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled,
}
