namespace KodaClaw.Contracts;

public sealed record SecretMigrationReport(
    DateTimeOffset GeneratedAt,
    string WorkspaceRootPath,
    string ArtifactPath,
    SecretMigrationSummary Summary,
    IReadOnlyList<SecretMigrationItem> Items);
