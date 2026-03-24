namespace KodaClaw.Contracts;

/// <summary>
/// status: "wait" | "scaned" | "confirmed" | "expired"
/// </summary>
public sealed record WeChatQrCodeStatus(string Status, string? BotToken);
