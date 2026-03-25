namespace KodaClaw.ChannelHub.Connectors.Telegram;

public interface ITelegramApiClient
{
    Task<TelegramUser> GetMeAsync(
        string botToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        string botToken,
        long? offset,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);

    Task<TelegramSendMessageResult> SendMessageAsync(
        string botToken,
        long chatId,
        string text,
        CancellationToken cancellationToken = default);

    Task<TelegramSendMessageResult> SendPhotoAsync(
        string botToken,
        long chatId,
        Stream photo,
        string contentType,
        string? caption,
        CancellationToken cancellationToken = default);

    Task<TelegramSendMessageResult> SendAudioAsync(
        string botToken,
        long chatId,
        Stream audio,
        string contentType,
        string? caption,
        CancellationToken cancellationToken = default);
}

