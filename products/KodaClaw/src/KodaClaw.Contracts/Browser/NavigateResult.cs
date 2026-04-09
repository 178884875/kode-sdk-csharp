namespace KodaClaw.Contracts;

public sealed record NavigateResult(string Url, string TabId, string? DeviceId = null);
