using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<RepairChecklistState>))]
public enum RepairChecklistState
{
    Pending,
    Completed,
}
