using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ChannelConnectorKind>))]
public enum ChannelConnectorKind
{
    Telegram = 0,
    GenericWebhook = 1,
    Feishu = 2,
    WeChat = 3,
    DingTalk = 4,
}
