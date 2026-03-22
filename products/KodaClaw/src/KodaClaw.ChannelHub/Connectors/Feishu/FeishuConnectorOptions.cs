namespace KodaClaw.ChannelHub.Connectors.Feishu;

public sealed record FeishuConnectorOptions(
    TimeSpan HeartbeatInterval,
    TimeSpan ReconnectBaseDelay,
    TimeSpan ReconnectMaxDelay)
{
    public FeishuConnectorOptions()
        : this(
            HeartbeatInterval: TimeSpan.FromSeconds(30),
            ReconnectBaseDelay: TimeSpan.FromSeconds(2),
            ReconnectMaxDelay: TimeSpan.FromSeconds(60))
    {
    }
}
