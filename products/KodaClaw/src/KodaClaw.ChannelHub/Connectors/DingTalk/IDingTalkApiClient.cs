using System.IO;

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

    /// <summary>群聊发送消息（orgGroupSend，sessionWebhook 过期时的 fallback）</summary>
    Task SendGroupMessageAsync(
        string accessToken,
        string robotCode,
        string openConversationId,
        string msgKey,
        string msgParam,
        CancellationToken cancellationToken = default);

    /// <summary>单聊发送媒体消息（图片/音频/视频/文件）</summary>
    Task SendMediaBatchAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string msgKey,
        string msgParam,
        CancellationToken cancellationToken = default);

        /// <summary>通过 sessionWebhook 直接发送消息（群聊优先路径）</summary>
    Task SendSessionWebhookMessageAsync(
        string webhookUrl,
        string msgKey,
        string msgParam,
        CancellationToken cancellationToken = default);

    /// <summary>单聊发送 ActionCard 整体跳转消息（msgKey=sampleActionCard）</summary>
    Task SendActionCardMessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string title,
        string text,
        string singleTitle,
        string singleUrl,
        CancellationToken cancellationToken = default);

    /// <summary>单聊发送 ActionCard 独立跳转多按钮消息（msgKey=sampleActionCard6）</summary>
    Task SendActionCard6MessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string title,
        string text,
        IReadOnlyList<DingTalkActionCardBtn> btns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取机器人收到的媒体文件下载 URL。
    /// POST /v1.0/robot/messageFiles/download
    /// </summary>
    /// <param name="accessToken">机器人 access_token</param>
    /// <param name="robotCode">机器人 appKey</param>
    /// <param name="downloadCode">消息 content 中的 downloadCode</param>
    Task<string> GetMediaDownloadUrlAsync(
        string accessToken,
        string robotCode,
        string downloadCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载媒体文件内容流（获取 downloadUrl 后直接 GET 下载）。
    /// </summary>
    Task<Stream> DownloadMediaAsync(
        string downloadUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传媒体文件到钉钉，返回 mediaId（用于出站发送图片/音频/视频/文件）。
    /// POST /v1.0/robot/messageResources/upload
    /// </summary>
    /// <param name="accessToken">机器人 access_token</param>
    /// <param name="robotCode">机器人 appKey</param>
    /// <param name="data">文件内容流</param>
    /// <param name="contentType">文件 MIME type（image/png, audio/mp3, video/mp4 等）</param>
    /// <param name="fileName">文件名（用于推断 type 参数）</param>
    Task<string> UploadMediaAsync(
        string accessToken,
        string robotCode,
        Stream data,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default);
}
