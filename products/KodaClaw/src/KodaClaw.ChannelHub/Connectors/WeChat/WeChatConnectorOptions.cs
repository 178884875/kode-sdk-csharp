namespace KodaClaw.ChannelHub.Connectors.WeChat;

public sealed record WeChatConnectorOptions
{
    /// <summary>内存 LRU 消息去重容量（message_id）</summary>
    public int LruDeduplicationSize { get; init; } = 500;

    /// <summary>iLink 会话过期（errcode -14）后的重试等待时间（毫秒）</summary>
    public int SessionExpiredRetryDelayMs { get; init; } = 30_000;

    /// <summary>网络/未知错误后的重试等待时间（毫秒）</summary>
    public int ErrorRetryDelayMs { get; init; } = 2_000;
}
