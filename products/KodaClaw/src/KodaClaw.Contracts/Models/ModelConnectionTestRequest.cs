namespace KodaClaw.Contracts;

public sealed record ModelConnectionTestRequest(
    string? PresetId,
    string? ModelId,
    string? BaseUrl,
    string ApiKey,
    string? Provider = null,
    string? EndpointId = null
);
