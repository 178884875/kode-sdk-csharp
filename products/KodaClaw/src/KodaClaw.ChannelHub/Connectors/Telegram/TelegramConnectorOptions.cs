namespace KodaClaw.ChannelHub.Connectors.Telegram;

public sealed record TelegramConnectorOptions(
    TimeSpan IdleDelay,
    TimeSpan ErrorRetryDelay,
    bool IgnoreNonTextMessages)
{
    public TelegramConnectorOptions()
        : this(
            IdleDelay: TimeSpan.FromMilliseconds(200),
            ErrorRetryDelay: TimeSpan.FromSeconds(1),
            IgnoreNonTextMessages: true)
    {
    }
}

