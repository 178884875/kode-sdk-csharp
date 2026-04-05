namespace KodaClaw.ChannelHub.Connectors.WeChat;

public interface IWeChatApiClient
{
    /// <summary>获取扫码登录二维码（无需 token）</summary>
    Task<ILinkQrCodeResponse> GetQrCodeAsync(CancellationToken ct = default);

    /// <summary>轮询二维码扫描状态（无需 token）</summary>
    Task<ILinkQrCodeStatusResponse> GetQrCodeStatusAsync(string qrcode, CancellationToken ct = default);

    /// <summary>验证 bot_token 是否有效（直接传 token，不依赖实例 BotToken）</summary>
    Task<ILinkLoginStatusResponse> CheckLoginStatusAsync(string botToken, CancellationToken ct = default);

    /// <summary>长轮询拉取新消息（需要 BotToken 已设置）</summary>
    Task<ILinkGetUpdatesResponse> GetUpdatesAsync(string syncBuf, CancellationToken ct = default);

    /// <summary>向指定用户发送文本消息（需要 BotToken 已设置）</summary>
    Task SendTextAsync(string toUserId, string contextToken, string text, CancellationToken ct = default);

    /// <summary>获取 typing_ticket，用于发送"正在输入"状态（需要 BotToken 已设置）</summary>
    Task<ILinkGetConfigResponse> GetConfigAsync(string ilinkUserId, string contextToken, CancellationToken ct = default);

    /// <summary>发送"正在输入"状态：status=1 开始，status=2 取消（需要 BotToken 已设置）</summary>
    Task SendTypingAsync(string ilinkUserId, string typingTicket, int status, CancellationToken ct = default);

    /// <summary>获取媒体 CDN 上传地址（需要 BotToken 已设置）</summary>
    Task<ILinkGetUploadUrlResponse> GetUploadUrlAsync(ILinkGetUploadUrlRequest request, CancellationToken ct = default);

    /// <summary>发送带媒体 item_list 的消息（需要 BotToken 已设置）</summary>
    Task SendMediaAsync(string toUserId, string contextToken,
        IReadOnlyList<ILinkMessageItem> items, CancellationToken ct = default);

    /// <summary>设置登录后的 BotToken（初始为 null）</summary>
    void SetBotToken(string botToken);
}
