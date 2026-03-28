using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<UpdateReleaseChannel>))]
public enum UpdateReleaseChannel
{
    Stable = 0,
    Preview = 1,
    Nightly = 2,
    Custom = 3,
}
