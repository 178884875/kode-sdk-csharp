namespace KodaClaw.ChannelHub.Connectors.Relay;

public sealed record RelayConnectorOptions(
    TimeSpan ReconnectBaseDelay,
    TimeSpan ReconnectMaxDelay,
    TimeSpan HeartbeatInterval,
    TimeSpan AuthTimeout)
{
    public RelayConnectorOptions()
        : this(
            ReconnectBaseDelay: TimeSpan.FromSeconds(2),
            ReconnectMaxDelay: TimeSpan.FromSeconds(60),
            HeartbeatInterval: TimeSpan.FromSeconds(30),
            AuthTimeout: TimeSpan.FromSeconds(10))
    {
    }
}
