namespace KodaClaw.Contracts;

public sealed record BrowserStatus(
    bool IsConnected,
    IReadOnlyList<BrowserDevice> Devices,
    DateTimeOffset? LastHeartbeat = null);
