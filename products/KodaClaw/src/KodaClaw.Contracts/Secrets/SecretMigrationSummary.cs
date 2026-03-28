namespace KodaClaw.Contracts;

public sealed record SecretMigrationSummary(
    int TotalCount,
    int MigratedCount,
    int LegacyFallbackCount,
    int MissingCount);
