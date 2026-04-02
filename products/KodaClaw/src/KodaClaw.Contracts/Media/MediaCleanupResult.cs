namespace KodaClaw.Contracts;

public record MediaCleanupResult(int DeletedCount, long FreedBytes, int PinnedCount);
