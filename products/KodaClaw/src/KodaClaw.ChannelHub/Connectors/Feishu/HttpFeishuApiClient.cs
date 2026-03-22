using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace KodaClaw.ChannelHub.Connectors.Feishu;

public sealed class HttpFeishuApiClient : IFeishuApiClient
{
    private const string BaseUrl = "https://open.feishu.cn";
    private static readonly TimeSpan TokenRefreshEarlyMargin = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    // 缓存的 token：(appId, tokenType) → (token, expiresAt)
    private readonly Dictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public HttpFeishuApiClient()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public async Task<string> GetTenantAccessTokenAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"tenant:{appId}";
        return await GetOrRefreshTokenAsync(cacheKey, async ct =>
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/open-apis/auth/v3/tenant_access_token/internal");
            request.Content = JsonContent.Create(
                new { app_id = appId, app_secret = appSecret },
                options: JsonOptions);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<FeishuTenantAccessTokenResponse>(JsonOptions, ct)
                .ConfigureAwait(false);

            if (result is null || result.Code != 0 || string.IsNullOrWhiteSpace(result.TenantAccessToken))
            {
                throw new InvalidOperationException(
                    $"Failed to get Feishu tenant_access_token: code={result?.Code}, msg={result?.Msg}");
            }

            return (result.TenantAccessToken, result.ExpireSeconds);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetAppAccessTokenAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"app:{appId}";
        return await GetOrRefreshTokenAsync(cacheKey, async ct =>
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/open-apis/auth/v3/app_access_token/internal");
            request.Content = JsonContent.Create(
                new { app_id = appId, app_secret = appSecret },
                options: JsonOptions);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<FeishuAppAccessTokenResponse>(JsonOptions, ct)
                .ConfigureAwait(false);

            if (result is null || result.Code != 0 || string.IsNullOrWhiteSpace(result.AppAccessToken))
            {
                throw new InvalidOperationException(
                    $"Failed to get Feishu app_access_token: code={result?.Code}, msg={result?.Msg}");
            }

            return (result.AppAccessToken, result.ExpireSeconds);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SendTextMessageAsync(
        string accessToken,
        string receiveId,
        string receiveIdType,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is required.", nameof(text));
        }

        var content = JsonSerializer.Serialize(new { text }, JsonOptions);
        using var request = BuildSendMessageRequest(accessToken, receiveId, receiveIdType, "text", content);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await ParseSendMessageResponse(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> UploadImageAsync(
        string accessToken,
        Stream image,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("message"), "image_type");
        var imageContent = new StreamContent(image);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(imageContent, "image", "image");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/open-apis/im/v1/images");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = form;

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<FeishuUploadImageResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (result is null || result.Code != 0 || string.IsNullOrWhiteSpace(result.Data?.ImageKey))
        {
            throw new InvalidOperationException(
                $"Feishu upload image failed: code={result?.Code}, msg={result?.Msg}");
        }

        return result.Data.ImageKey;
    }

    public async Task<string> SendImageMessageAsync(
        string accessToken,
        string receiveId,
        string receiveIdType,
        string imageKey,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        // 飞书图片消息不支持 caption；若有 caption 先发图再发文字
        var content = JsonSerializer.Serialize(new { image_key = imageKey }, JsonOptions);
        using var request = BuildSendMessageRequest(accessToken, receiveId, receiveIdType, "image", content);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var messageId = await ParseSendMessageResponse(response, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(caption))
        {
            await SendTextMessageAsync(accessToken, receiveId, receiveIdType, caption, cancellationToken)
                .ConfigureAwait(false);
        }

        return messageId;
    }

    public async Task<FeishuWsEndpoint> GetWsEndpointAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        // 注意：此 API 路径是 /callback/ws/endpoint，不在 /open-apis/ 下。
        // 请求体字段名是 AppID / AppSecret（Pascal case）。
        using var request = new HttpRequestMessage(HttpMethod.Post, "/callback/ws/endpoint");
        // 飞书要求 Pascal case 字段名（AppID / AppSecret），
        // 直接用 StringContent 避免 JsonContent 附加 charset 或命名策略干扰。
        var bodyJson = System.Text.Json.JsonSerializer.Serialize(new { AppID = appId, AppSecret = appSecret });
        request.Content = new StringContent(bodyJson);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("locale", "zh");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"GET /callback/ws/endpoint failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        using var doc = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = doc.RootElement;

        if (!root.TryGetProperty("code", out var codeProp) || codeProp.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "unknown";
            throw new InvalidOperationException($"Failed to get Feishu WS endpoint: {msg}");
        }

        var data = root.GetProperty("data");
        var url = data.GetProperty("URL").GetString()
            ?? throw new InvalidOperationException("Feishu WS endpoint URL is empty.");

        var cfg = data.GetProperty("ClientConfig");
        return new FeishuWsEndpoint(
            Url: url,
            PingIntervalSeconds: cfg.TryGetProperty("PingInterval", out var pi) ? pi.GetInt32() : 90,
            ReconnectCount: cfg.TryGetProperty("ReconnectCount", out var rc) ? rc.GetInt32() : -1,
            ReconnectIntervalSeconds: cfg.TryGetProperty("ReconnectInterval", out var ri) ? ri.GetInt32() : 90,
            ReconnectNonceSeconds: cfg.TryGetProperty("ReconnectNonce", out var rn) ? rn.GetInt32() : 25);
    }

    private HttpRequestMessage BuildSendMessageRequest(
        string accessToken,
        string receiveId,
        string receiveIdType,
        string msgType,
        string content)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/open-apis/im/v1/messages?receive_id_type={receiveIdType}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(
            new
            {
                receive_id = receiveId,
                msg_type = msgType,
                content,
            },
            options: JsonOptions);
        return request;
    }

    private static async Task<string> ParseSendMessageResponse(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var result = await response.Content
            .ReadFromJsonAsync<FeishuSendMessageResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (result is null || result.Code != 0)
        {
            throw new InvalidOperationException(
                $"Feishu send message failed: code={result?.Code}, msg={result?.Msg}");
        }

        return result.Data?.MessageId ?? string.Empty;
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
