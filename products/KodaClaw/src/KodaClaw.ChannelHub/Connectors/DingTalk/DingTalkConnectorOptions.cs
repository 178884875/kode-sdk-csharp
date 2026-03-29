namespace KodaClaw.ChannelHub.Connectors.DingTalk;

public sealed record DingTalkConnectorOptions(
    TimeSpan ReconnectBaseDelay,
    TimeSpan ReconnectMaxDelay)
{
    public DingTalkConnectorOptions()
        : this(
            ReconnectBaseDelay: TimeSpan.FromSeconds(2),
            ReconnectMaxDelay: TimeSpan.FromSeconds(60))
    {
    }
}
