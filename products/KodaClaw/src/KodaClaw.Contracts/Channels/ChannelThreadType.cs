using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ChannelThreadType>))]
public enum ChannelThreadType
{
    DirectMessage = 0,
    Group = 1,
}
