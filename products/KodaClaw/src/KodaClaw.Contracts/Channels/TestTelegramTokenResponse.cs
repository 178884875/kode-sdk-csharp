namespace KodaClaw.Contracts;

public sealed record TestTelegramTokenResponse(
    bool Ok,
    string? BotName = null,
    string? BotUsername = null,
    string? Error = null);
