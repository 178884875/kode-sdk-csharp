namespace KodaClaw.Contracts;

public sealed record UpdateCheckRequest(
    string? DesktopCurrentVersion = null,
    UpdateReleaseChannel? DesktopReleaseChannel = null);
