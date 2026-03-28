namespace KodaClaw.Contracts;

public sealed record DiagnosticBundleDesktopContext(
    bool DesktopMode,
    string Platform,
    string AppVersion,
    UpdateReleaseChannel ReleaseChannel,
    string? GatewayLifecycleMode = null);
