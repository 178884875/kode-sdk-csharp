namespace KodaClaw.Contracts;

public sealed record BackupExportRequest(
    string? ArchivePath = null);
