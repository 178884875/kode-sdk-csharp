namespace KodaClaw.Contracts;

public sealed record SystemStatsEvent(
    int ErrorCount,
    int WarningCount,
    int InboxUnreadCount);
