namespace KodaClaw.Contracts;

public sealed record ChannelIdentity(
    string Id,
    string? Username = null,
    string? DisplayName = null,
    bool IsBot = false,
    string? MetadataJson = null);
