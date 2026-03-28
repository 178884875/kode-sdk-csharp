namespace KodaClaw.Contracts;

public sealed record DiagnosticBundleManifestEntry(
    string Path,
    string Sha256,
    long SizeBytes,
    string Category);
