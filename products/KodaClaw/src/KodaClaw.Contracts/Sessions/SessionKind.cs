using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<SessionKind>))]
public enum SessionKind
{
    Main = 0,
    ChannelDirectMessage = 1,
    ChannelGroup = 2,
    Automation = 3,
    Plugin = 4,
}
