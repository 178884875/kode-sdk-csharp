using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace KodaClaw.ChannelHub.Connectors.WeChat;

/// <summary>
/// iLink CDN 客户端：AES-128-ECB 加解密 + HTTP 上传/下载。
///
/// Key 编码规则（来源：wechat_cdn.py parse_aes_key）：
///   图片（type=2）：aes_key = base64(raw 16 bytes)
///   文件/视频/语音（type=3/4/5）：aes_key = base64(hex string of 16 bytes)
/// </summary>
internal sealed class HttpWeChatCdnClient(
    ILogger<HttpWeChatCdnClient> logger) : IWeChatCdnClient
{
    // CDN 不走代理（与 HttpWeChatApiClient 相同理由）
    private static readonly HttpClient _httpClient = new(new HttpClientHandler { UseProxy = false });

    public async Task<Stream> DownloadAndDecryptAsync(string cdnBaseUrl, string encryptQueryParam,
        string aesKeyBase64, bool isImage, CancellationToken ct)
    {
        var url = $"{cdnBaseUrl.TrimEnd('/')}/{encryptQueryParam}";
        var client = _httpClient;

        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var encryptedBytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var keyBytes = ParseAesKey(aesKeyBase64, isImage);
        var plainBytes = AesEcbDecrypt(encryptedBytes, keyBytes);

        return new MemoryStream(plainBytes);
    }

    public async Task<string> UploadEncryptedAsync(ILinkUploadParam uploadParam,
        byte[] encryptedBytes, CancellationToken ct)
    {
        var client = _httpClient;
        using var content = new ByteArrayContent(encryptedBytes);

        if (uploadParam.Headers != null)
            foreach (var (k, v) in uploadParam.Headers)
                content.Headers.TryAddWithoutValidation(k, v);

        HttpResponseMessage response;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            response = await client.PostAsync(uploadParam.Url, content, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                if (response.Headers.TryGetValues("x-encrypted-param", out var values))
                    return values.First();

                logger.LogWarning("CDN upload succeeded but x-encrypted-param header is missing");
                throw new InvalidOperationException("CDN upload response missing x-encrypted-param header");
            }

            if ((int)response.StatusCode >= 500 && attempt < 2)
            {
                logger.LogWarning("CDN upload attempt {Attempt} failed with {Status}, retrying",
                    attempt + 1, response.StatusCode);
                await Task.Delay(500 * (attempt + 1), ct).ConfigureAwait(false);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            logger.LogError("CDN upload failed: {Status} {Body}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        throw new InvalidOperationException("CDN upload failed after 3 attempts");
    }

    // ── AES-ECB 加解密 ────────────────────────────────────────

    /// <summary>加密，返回 PKCS7 padding 后的密文。</summary>
    internal static byte[] AesEcbEncrypt(byte[] plain, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plain, 0, plain.Length);
    }

    /// <summary>解密，去除 PKCS7 padding 后返回原始字节。</summary>
    internal static byte[] AesEcbDecrypt(byte[] cipher, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    /// <summary>
    /// 解析 iLink aes_key 字段：
    ///   图片：base64(raw 16 bytes)  → 直接 FromBase64
    ///   文件/视频/语音：base64(hex string of 16 bytes) → FromBase64 → hex string → FromHex
    /// </summary>
    internal static byte[] ParseAesKey(string aesKeyBase64, bool isImage)
    {
        var decoded = Convert.FromBase64String(aesKeyBase64);
        if (isImage)
            return decoded;

        var hex = Encoding.UTF8.GetString(decoded);
        return Convert.FromHexString(hex);
    }
}
