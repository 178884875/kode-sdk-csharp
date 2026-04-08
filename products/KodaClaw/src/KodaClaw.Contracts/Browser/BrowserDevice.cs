namespace KodaClaw.Contracts;

public sealed record BrowserDevice(
    string DeviceId,
    string Label,
    DateTimeOffset PairedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastSeenAt = null,
    bool IsOnline = false);
