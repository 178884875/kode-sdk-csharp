namespace KodaClaw.ChannelHub.Connectors.Feishu;

public interface IFeishuApiClient
{
    /// <summary>获取 tenant_access_token（用于发消息）</summary>
    Task<string> GetTenantAccessTokenAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken = default);

    /// <summary>获取 app_access_token（用于 WS 长连接认证）</summary>
    Task<string> GetAppAccessTokenAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 WebSocket 长连接 endpoint URL 及客户端配置。
    /// POST /callback/ws/endpoint with {AppID, AppSecret}。
    /// </summary>
    Task<FeishuWsEndpoint> GetWsEndpointAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken = default);

    /// <summary>发送文本消息</summary>
    Task<string> SendTextMessageAsync(
        string accessToken,
        string receiveId,
        string receiveIdType,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>上传图片，返回 image_key</summary>
    Task<string> UploadImageAsync(
        string accessToken,
        Stream image,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>发送图片消息（使用已上传的 image_key）</summary>
    Task<string> SendImageMessageAsync(
        string accessToken,
        string receiveId,
        string receiveIdType,
        string imageKey,
        string? caption,
        CancellationToken cancellationToken = default);

    /// <summary>上传音频文件，返回 file_key</summary>
    Task<string> UploadAudioFileAsync(
        string accessToken,
        Stream audio,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>发送音频消息（使用已上传的 file_key）</summary>
    Task<string> SendAudioMessageAsync(
        string accessToken,
        string receiveId,
        string receiveIdType,
        string fileKey,
        string? caption,
        CancellationToken cancellationToken = default);
}
