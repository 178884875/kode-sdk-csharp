namespace KodaClaw.ChannelHub.Connectors.DingTalk;

public interface IDingTalkApiClient
{
    /// <summary>获取 access_token（用于发消息等 REST API 调用）</summary>
    Task<string> GetAccessTokenAsync(
        string appKey,
        string appSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 开启 Stream 长连接，返回 wss:// endpoint。
    /// POST /v1.0/gateway/connections/open
    /// </summary>
    Task<DingTalkOpenConnectionResponse> OpenStreamConnectionAsync(
        string appKey,
        string appSecret,
        CancellationToken cancellationToken = default);

    /// <summary>单聊批量发送文本消息</summary>
    Task SendTextMessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>单聊批量发送 Markdown 消息</summary>
    Task SendMarkdownMessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string title,
        string text,
        CancellationToken cancellationToken = default);
}
