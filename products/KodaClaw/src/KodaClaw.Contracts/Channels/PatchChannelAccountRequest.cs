namespace KodaClaw.Contracts;

public sealed record PatchChannelAccountRequest(
    string? DisplayName = null,
    DeliveryMode? DeliveryMode = null,
    bool? Enabled = null);
