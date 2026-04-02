using System.IO;
using System.Net.Http.Json;
using System.Text.Json;

namespace KodaClaw.ChannelHub.Connectors.DingTalk;

public sealed class HttpDingTalkApiClient : IDingTalkApiClient
{
    private const string BaseUrl = "https://api.dingtalk.com";
    private static readonly TimeSpan TokenRefreshEarlyMargin = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    // 缓存的 token：appKey → (token, expiresAt)
    private readonly Dictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public HttpDingTalkApiClient()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    /// <summary>供单元测试注入自定义 HttpClient（如 FakeHttpMessageHandler）</summary>
    internal HttpDingTalkApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetAccessTokenAsync(
        string appKey,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        return await GetOrRefreshTokenAsync(appKey, async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1.0/oauth2/accessToken");
            request.Content = JsonContent.Create(
                new { appKey, appSecret },
                options: JsonOptions);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"DingTalk GetAccessToken failed: {(int)response.StatusCode}. Body: {body}");
            }

            var result = await response.Content
                .ReadFromJsonAsync<DingTalkAccessTokenResponse>(JsonOptions, ct)
                .ConfigureAwait(false);

            if (result is null || string.IsNullOrWhiteSpace(result.AccessToken))
            {
                throw new InvalidOperationException("Failed to get DingTalk access_token: empty response.");
            }

            return (result.AccessToken, result.ExpireIn > 0 ? result.ExpireIn : 7200);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DingTalkOpenConnectionResponse> OpenStreamConnectionAsync(
        string appKey,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(appKey, appSecret, cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1.0/gateway/connections/open");
        request.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        request.Content = JsonContent.Create(
            new
            {
                clientId = appKey,
                clientSecret = appSecret,
                subscriptions = new[]
                {
                    new { type = "CALLBACK", topic = "/v1.0/im/bot/messages/get" },
                },
                ua = "kodaclaw-dingtalk/1.0",
            },
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"DingTalk OpenStreamConnection failed: {(int)response.StatusCode}. Body: {body}");
        }

        var result = await response.Content
            .ReadFromJsonAsync<DingTalkOpenConnectionResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (result is null || string.IsNullOrWhiteSpace(result.Endpoint))
        {
            throw new InvalidOperationException("DingTalk OpenStreamConnection returned empty endpoint.");
        }

        return result;
    }

    public async Task SendTextMessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string text,
        CancellationToken cancellationToken = default)
    {
        var msgParam = JsonSerializer.Serialize(new { content = text }, JsonOptions);
        await SendBatchMessageAsync(accessToken, robotCode, userIds, "sampleText", msgParam, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SendMarkdownMessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string title,
        string text,
        CancellationToken cancellationToken = default)
    {
        var msgParam = JsonSerializer.Serialize(new { title, text }, JsonOptions);
        await SendBatchMessageAsync(accessToken, robotCode, userIds, "sampleMarkdown", msgParam, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SendGroupMessageAsync(
        string accessToken,
        string robotCode,
        string openConversationId,
        string msgKey,
        string msgParam,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/v1.0/robot/groupMessages/send");
        request.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        request.Content = JsonContent.Create(
            new { robotCode, openConversationId, msgKey, msgParam },
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"DingTalk SendGroupMessage failed: {(int)response.StatusCode}. Body: {body}");
        }
    }

    public async Task SendSessionWebhookMessageAsync(
        string webhookUrl,
        string msgKey,
        string msgParam,
        CancellationToken cancellationToken = default)
    {
        // sessionWebhook 是完整绝对 URL，直接 POST（绝对 URI 优先于 BaseAddress）
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(webhookUrl, UriKind.Absolute));
        request.Content = JsonContent.Create(
            new { msgKey, msgParam },
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"DingTalk SendSessionWebhook failed: {(int)response.StatusCode}. Body: {body}");
        }
    }

    public async Task SendActionCardMessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string title,
        string text,
        string singleTitle,
        string singleUrl,
        CancellationToken cancellationToken = default)
    {
        var msgParam = JsonSerializer.Serialize(
            new { title, text, singleTitle, singleURL = singleUrl }, JsonOptions);
        await SendBatchMessageAsync(accessToken, robotCode, userIds, "sampleActionCard", msgParam, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SendActionCard6MessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string title,
        string text,
        IReadOnlyList<DingTalkActionCardBtn> btns,
        CancellationToken cancellationToken = default)
    {
        var btnsArray = btns.Select(b => new { title = b.Title, actionURL = b.ActionUrl }).ToArray();
        var msgParam = JsonSerializer.Serialize(new { title, text, btns = btnsArray }, JsonOptions);
        await SendBatchMessageAsync(accessToken, robotCode, userIds, "sampleActionCard6", msgParam, cancellationToken)
            .ConfigureAwait(false);
    }


    public async Task<string> GetMediaDownloadUrlAsync(
        string accessToken,
        string robotCode,
        string downloadCode,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/v1.0/robot/messageFiles/download");
        request.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        request.Content = JsonContent.Create(
            new { robotCode, downloadCode },
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"DingTalk GetMediaDownloadUrl failed: {(int)response.StatusCode}. Body: {body}");
        }

        var result = await response.Content
            .ReadFromJsonAsync<DingTalkDownloadFileResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (result is null || string.IsNullOrWhiteSpace(result.DownloadUrl))
        {
            throw new InvalidOperationException(
                "DingTalk GetMediaDownloadUrl returned empty downloadUrl.");
        }

        return result.DownloadUrl;
    }

    public async Task<Stream> DownloadMediaAsync(
        string downloadUrl,
        CancellationToken cancellationToken = default)
    {
        // downloadUrl is absolute, bypass BaseAddress
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(downloadUrl, UriKind.Absolute));

        var response = await _httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"DingTalk DownloadMedia failed: {(int)response.StatusCode}. Body: {body}");
        }

        // Caller must dispose the stream
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }


    public async Task<string> UploadMediaAsync(
        string accessToken,
        string robotCode,
        Stream data,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // 根据 contentType 推断钉钉的 type 参数
        var dingType = InferDingMediaType(contentType);

        // 手动构建 multipart 请求体，避免 .NET 生成 RFC 5987 格式（钉钉老版 API 不兼容）
        var boundary = Guid.NewGuid().ToString("N");
        using var bodyStream = new MemoryStream();
        using (var writer = new System.IO.StreamWriter(bodyStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // media part
            await writer.WriteAsync($"--{boundary}\r\n");
            await writer.WriteAsync($"Content-Disposition: form-data; name=\"media\"; filename=\"{fileName}\"\r\n");
            await writer.WriteAsync("Content-Type: application/octet-stream\r\n");
            await writer.WriteAsync("\r\n");
            await writer.FlushAsync();

            await data.CopyToAsync(bodyStream, cancellationToken).ConfigureAwait(false);

            await writer.WriteAsync("\r\n");

            // type part
            await writer.WriteAsync($"--{boundary}\r\n");
            await writer.WriteAsync("Content-Disposition: form-data; name=\"type\"\r\n");
            await writer.WriteAsync("\r\n");
            await writer.WriteAsync(dingType);
            await writer.WriteAsync("\r\n");

            // closing boundary
            await writer.WriteAsync($"--{boundary}--\r\n");
            await writer.FlushAsync();
        }

        bodyStream.Position = 0;

        // 使用独立 HttpClient，避免 BaseAddress 干扰，走钉钉老版 API
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://oapi.dingtalk.com/media/upload?access_token={accessToken}");

        request.Content = new StreamContent(bodyStream);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/form-data")
            {
                Parameters = { new System.Net.Http.Headers.NameValueHeaderValue("boundary", boundary) }
            };

        using var response = await httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"DingTalk UploadMedia failed: {(int)response.StatusCode}. Body: {rawBody}");
        }

        var result = System.Text.Json.JsonSerializer.Deserialize<DingTalkUploadMediaResponse>(rawBody);

        if (result is null || string.IsNullOrWhiteSpace(result.MediaId))
        {
            throw new InvalidOperationException(
                $"DingTalk UploadMedia returned empty mediaId. Body: {rawBody}");
        }

        return result.MediaId;
    }

    private static string InferDingMediaType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return "file";

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "image";
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return "voice";
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return "video";
        return "file";
    }


    public async Task SendMediaBatchAsync(string accessToken, string robotCode, IReadOnlyList<string> userIds, string msgKey, string msgParam, CancellationToken cancellationToken = default)
    {
        await SendBatchMessageAsync(accessToken, robotCode, userIds, msgKey, msgParam, cancellationToken);
    }

    private async Task SendBatchMessageAsync(
        string accessToken,
        string robotCode,
        IReadOnlyList<string> userIds,
        string msgKey,
        string msgParam,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1.0/robot/oToMessages/batchSend");
        request.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        request.Content = JsonContent.Create(
            new
            {
                robotCode,
                userIds,
                msgKey,
                msgParam,
            },
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"DingTalk SendMessage failed: {(int)response.StatusCode}. Body: {body}");
        }
    }

    private async Task<string> GetOrRefreshTokenAsync(
        string cacheKey,
        Func<CancellationToken, Task<(string Token, int ExpireSeconds)>> fetchAsync,
        CancellationToken cancellationToken)
    {
        // 快速路径：无锁检查缓存
        if (_tokenCache.TryGetValue(cacheKey, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow + TokenRefreshEarlyMargin)
        {
            return cached.Token;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 双重检查
            if (_tokenCache.TryGetValue(cacheKey, out cached)
                && cached.ExpiresAt > DateTimeOffset.UtcNow + TokenRefreshEarlyMargin)
            {
                return cached.Token;
            }

            var (token, expireSeconds) = await fetchAsync(cancellationToken).ConfigureAwait(false);
            _tokenCache[cacheKey] = (token, DateTimeOffset.UtcNow.AddSeconds(expireSeconds));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
