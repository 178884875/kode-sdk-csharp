using System.Net.Http.Json;
using System.Text.Json;

namespace KodaClaw.ChannelHub.Connectors.Telegram;

public sealed class HttpTelegramApiClient : ITelegramApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public HttpTelegramApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<TelegramUser> GetMeAsync(
        string botToken,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync<TelegramUser>(
            botToken,
            method: "getMe",
            payload: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        string botToken,
        long? offset,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync<List<TelegramUpdate>>(
            botToken,
            method: "getUpdates",
            payload: new
            {
                offset,
                timeout = timeoutSeconds,
                allowed_updates = new[] { "message", "edited_message" },
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TelegramSendMessageResult> SendMessageAsync(
        string botToken,
        long chatId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("A non-empty telegram message text is required.", nameof(text));
        }

        return await SendAsync<TelegramSendMessageResult>(
            botToken,
            method: "sendMessage",
            payload: new
            {
                chat_id = chatId,
                text,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(
        string botToken,
        string method,
        object? payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
        {
            throw new ArgumentException("A non-empty telegram bot token is required.", nameof(botToken));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildEndpoint(botToken, method));
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content
            .ReadFromJsonAsync<TelegramApiResponse<T>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (envelope is null)
        {
            throw new InvalidOperationException($"Telegram API '{method}' returned an empty response payload.");
        }

        if (!envelope.Ok || envelope.Result is null)
        {
            throw new InvalidOperationException(
                $"Telegram API '{method}' failed: {envelope.Description ?? "unknown error"}");
        }

        return envelope.Result;
    }

    private static string BuildEndpoint(string botToken, string method)
    {
        return $"https://api.telegram.org/bot{botToken.Trim()}/{method}";
    }
}

