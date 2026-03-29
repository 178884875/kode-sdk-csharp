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
