namespace KodaClaw.ChannelHub.Connectors.WeChat;

public interface IWeChatCdnClient
{
    /// <summary>
    /// 从 CDN 下载并 AES-ECB 解密。返回解密后的原始字节流。
    /// </summary>
    Task<Stream> DownloadAndDecryptAsync(string cdnBaseUrl, string encryptQueryParam,
        string aesKeyBase64, bool isImage, CancellationToken ct);

    /// <summary>
    /// 将已加密的字节上传到 CDN，返回 encrypt_query_param（来自响应 Header x-encrypted-param）。
    /// </summary>
    Task<string> UploadEncryptedAsync(ILinkUploadParam uploadParam,
        byte[] encryptedBytes, CancellationToken ct);
}
