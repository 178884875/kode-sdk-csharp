using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

public sealed record TabInfo(
    [property: JsonPropertyName("id")] string TabId,
    string Url,
    string Title,
    bool IsActive = false,
    string? DeviceId = null);
