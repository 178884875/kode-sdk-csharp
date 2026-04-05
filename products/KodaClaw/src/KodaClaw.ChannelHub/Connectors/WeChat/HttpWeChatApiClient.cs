using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KodaClaw.ChannelHub.Connectors.WeChat;

public sealed class HttpWeChatApiClient : IWeChatApiClient
{
    internal const string BaseUrl = "https://ilinkai.weixin.qq.com";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // 每次请求生成新的随机 UIN（vibe-remote 的做法，静态 UIN 会触发 412）
    private static string NewWeChatUin() =>
        Convert.ToBase64String(BitConverter.GetBytes((uint)Random.Shared.Next()));

    private readonly HttpClient _httpClient;
    private string? _botToken;

    public HttpWeChatApiClient()
    {
        // 绕过系统代理：iLink API 使用私有域名，系统代理（Proxyman/Charles 等）
        // 无法为其签发证书，会导致 TLS 握手失败。
        // 禁用自动解压：HttpClientHandler 默认添加 Accept-Encoding: gzip, br，
        // iLink 的长轮询端点不支持压缩流，会返回 412 Precondition Failed。
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
    }

    public void SetBotToken(string botToken)
    {
        _botToken = botToken;
    }

    // ── 登录流程（无需 BotToken）─────────────────────────────

    public async Task<ILinkQrCodeResponse> GetQrCodeAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/ilink/bot/get_bot_qrcode?bot_type=3");
        AddCommonHeaders(request, includeAuth: false);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // iLink 返回 application/octet-stream，必须先读 string 再反序列化
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<ILinkQrCodeResponse>(body, JsonOptions);
        return result ?? throw new InvalidOperationException("Empty response from get_bot_qrcode");
    }

    public async Task<ILinkQrCodeStatusResponse> GetQrCodeStatusAsync(string qrcode, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/ilink/bot/get_qrcode_status?qrcode={Uri.EscapeDataString(qrcode)}");
        AddCommonHeaders(request, includeAuth: false);
        request.Headers.TryAddWithoutValidation("iLink-App-ClientVersion", "1");

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<ILinkQrCodeStatusResponse>(body, JsonOptions);
        return result ?? throw new InvalidOperationException("Empty response from get_qrcode_status");
    }

    public async Task<ILinkLoginStatusResponse> CheckLoginStatusAsync(string botToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/getLoginStatus");
        // 注意：此接口不用通用 auth header，直接在 body 传 token
        var loginJson = JsonSerializer.Serialize(new { token = botToken }, JsonOptions);
        request.Content = new StringContent(loginJson, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<ILinkLoginStatusResponse>(body, JsonOptions);
        return result ?? throw new InvalidOperationException("Empty response from getLoginStatus");
    }

    // ── 消息收发（需要 BotToken）────────────────────────────

    public async Task<ILinkGetUpdatesResponse> GetUpdatesAsync(string syncBuf, CancellationToken ct = default)
    {
        EnsureBotToken();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ilink/bot/getupdates");
        // 严格对齐 vibe-remote：每次请求生成新随机 UIN；Content-Type 不带 charset
        // Connection: close 防止 keep-alive 连接被服务端关闭后复用导致 ResponseEnded
        request.Headers.ConnectionClose = true;
        request.Headers.TryAddWithoutValidation("AuthorizationType", "ilink_bot_token");
        request.Headers.TryAddWithoutValidation("X-WECHAT-UIN", NewWeChatUin());
        if (_botToken is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_botToken}");
        var json = JsonSerializer.Serialize(
            new { get_updates_buf = syncBuf, base_info = new { channel_version = "kodaclaw" } },
            JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"getupdates failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase} — {errBody}",
                null,
                response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<ILinkGetUpdatesResponse>(body, JsonOptions);
        return result ?? throw new InvalidOperationException("Empty response from getupdates");
    }

    public async Task SendTextAsync(string toUserId, string contextToken, string text, CancellationToken ct = default)
    {
        EnsureBotToken();

        var body = new ILinkSendMessageRequest
        {
            Msg = new ILinkSendMessageBody
            {
                ToUserId = toUserId,
                ClientId = $"kodaclaw-{Guid.NewGuid():N}",
                ContextToken = contextToken,
                ItemList = [new ILinkMessageItem { Type = 1, TextItem = new ILinkTextItem { Text = text } }]
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ilink/bot/sendmessage");
        AddCommonHeaders(request, includeAuth: true);
        // 与 getupdates 相同：Content-Type 不能带 charset，否则 iLink 返回 412
        var json = JsonSerializer.Serialize(body, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ILinkGetConfigResponse> GetConfigAsync(string ilinkUserId, string contextToken, CancellationToken ct = default)
    {
        EnsureBotToken();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ilink/bot/getconfig");
        AddCommonHeaders(request, includeAuth: true);
        var json = JsonSerializer.Serialize(
            new { ilink_user_id = ilinkUserId, context_token = contextToken },
            JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<ILinkGetConfigResponse>(body, JsonOptions);
        return result ?? throw new InvalidOperationException("Empty response from getconfig");
    }

    public async Task SendTypingAsync(string ilinkUserId, string typingTicket, int status, CancellationToken ct = default)
    {
        EnsureBotToken();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ilink/bot/sendtyping");
        AddCommonHeaders(request, includeAuth: true);
        var json = JsonSerializer.Serialize(
            new { ilink_user_id = ilinkUserId, typing_ticket = typingTicket, status },
            JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ILinkGetUploadUrlResponse> GetUploadUrlAsync(ILinkGetUploadUrlRequest uploadRequest, CancellationToken ct = default)
    {
        EnsureBotToken();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ilink/bot/getuploadurl");
        AddCommonHeaders(request, includeAuth: true);
        var json = JsonSerializer.Serialize(uploadRequest, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"getuploadurl failed: HTTP {(int)response.StatusCode} — {errBody}",
                null, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<ILinkGetUploadUrlResponse>(body, JsonOptions);
        return result ?? throw new InvalidOperationException("Empty response from getuploadurl");
    }

    public async Task SendMediaAsync(string toUserId, string contextToken,
        IReadOnlyList<ILinkMessageItem> items, CancellationToken ct = default)
    {
        EnsureBotToken();

        var body = new ILinkSendMessageRequest
        {
            Msg = new ILinkSendMessageBody
            {
                ToUserId = toUserId,
                ClientId = $"kodaclaw-{Guid.NewGuid():N}",
                ContextToken = contextToken,
                ItemList = items
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ilink/bot/sendmessage");
        AddCommonHeaders(request, includeAuth: true);
        var json = JsonSerializer.Serialize(body, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // ── 内部工具 ─────────────────────────────────────────────

    private void AddCommonHeaders(HttpRequestMessage request, bool includeAuth)
    {
        request.Headers.TryAddWithoutValidation("AuthorizationType", "ilink_bot_token");
        request.Headers.TryAddWithoutValidation("X-WECHAT-UIN", NewWeChatUin());

        if (includeAuth && _botToken is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_botToken}");
    }

    private void EnsureBotToken()
    {
        if (string.IsNullOrEmpty(_botToken))
            throw new InvalidOperationException("WeChatApiClient: BotToken is not set. Call SetBotToken() after login.");
    }
}
