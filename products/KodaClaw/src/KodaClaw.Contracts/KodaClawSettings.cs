namespace KodaClaw.Contracts;

public sealed record KodaClawSettings(
    string DefaultLandingRoute,
    ThemeMode Theme,
    bool RequireApprovalForExternalActions,
    bool NotificationsEnabled,
    bool QuietHoursEnabled,
    string? QuietHoursStartLocalTime,
    string? QuietHoursEndLocalTime,
    DateTimeOffset UpdatedAt)
{
    public static KodaClawSettings Default { get; } = new(
        DefaultLandingRoute: "/chat",
        Theme: ThemeMode.System,
        RequireApprovalForExternalActions: true,
        NotificationsEnabled: true,
        QuietHoursEnabled: false,
        QuietHoursStartLocalTime: null,
        QuietHoursEndLocalTime: null,
        UpdatedAt: DateTimeOffset.UnixEpoch);
}
