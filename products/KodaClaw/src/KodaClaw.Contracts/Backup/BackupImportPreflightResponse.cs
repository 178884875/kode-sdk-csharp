namespace KodaClaw.Contracts;

public sealed record BackupImportPreflightResponse(
    DateTimeOffset EvaluatedAt,
    string WorkspaceRootPath,
    string ArchivePath,
    BackupManifest Manifest,
    bool ChecksumVerified,
    bool CanImport,
    RepairChecklist Checklist);
