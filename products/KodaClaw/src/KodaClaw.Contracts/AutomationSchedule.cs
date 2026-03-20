namespace KodaClaw.Contracts;

public sealed record AutomationSchedule(
    AutomationScheduleKind Kind,
    int? Interval,
    string? LocalTime,
    IReadOnlyList<AutomationScheduleDay>? DaysOfWeek);
