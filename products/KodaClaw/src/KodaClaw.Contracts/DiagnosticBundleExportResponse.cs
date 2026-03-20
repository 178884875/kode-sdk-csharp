namespace KodaClaw.Contracts;

public sealed record DiagnosticBundleExportResponse(
    DateTimeOffset GeneratedAt,
    string WorkspaceRootPath,
    string BundlePath,
    DiagnosticBundleManifest Manifest);
