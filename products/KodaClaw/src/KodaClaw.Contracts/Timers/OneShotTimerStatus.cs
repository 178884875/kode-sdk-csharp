using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<OneShotTimerStatus>))]
public enum OneShotTimerStatus
{
    Pending,
    Fired,
    Cancelled,
}
