using System.Globalization;
using KodaClaw.Contracts;

namespace KodaClaw.Automation;

internal static class AutomationValidation
{
    private const string LocalTimeFormat = "HH:mm";

    public static void ValidateDefinition(AutomationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateRequired(definition.Id, nameof(definition.Id));
        ValidateRequired(definition.Title, nameof(definition.Title));
        ValidateRequired(definition.Prompt, nameof(definition.Prompt));
        ValidateSchedule(definition.Schedule, nameof(definition.Schedule));

        if (definition.CreatedAt == default)
        {
            throw new ArgumentException("CreatedAt is required.", nameof(definition.CreatedAt));
        }

        if (definition.UpdatedAt == default)
        {
            throw new ArgumentException("UpdatedAt is required.", nameof(definition.UpdatedAt));
        }
    }

    public static void ValidateRunRecord(AutomationRunRecord runRecord)
    {
        ArgumentNullException.ThrowIfNull(runRecord);
        ValidateRequired(runRecord.RunId, nameof(runRecord.RunId));
        ValidateRequired(runRecord.AutomationId, nameof(runRecord.AutomationId));
        ValidateRequired(runRecord.Trigger, nameof(runRecord.Trigger));

        if (runRecord.Attempt < 1)
        {
            throw new ArgumentException("Attempt must be greater than or equal to 1.", nameof(runRecord.Attempt));
        }

        if (runRecord.StartedAt == default)
        {
            throw new ArgumentException("StartedAt is required.", nameof(runRecord.StartedAt));
        }

        if (runRecord.CompletedAt is { } completedAt && completedAt < runRecord.StartedAt)
        {
            throw new ArgumentException(
                "CompletedAt must be greater than or equal to StartedAt.",
                nameof(runRecord.CompletedAt));
        }
    }

    public static void ValidateId(string? id, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id is required.", parameterName);
        }
    }

    public static int NormalizeLimit(int limit, int defaultLimit = 50, int maxLimit = 200)
    {
        if (limit <= 0)
        {
            return defaultLimit;
        }

        return Math.Min(limit, maxLimit);
    }

    private static void ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static void ValidateSchedule(AutomationSchedule schedule, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        switch (schedule.Kind)
        {
            case AutomationScheduleKind.Hourly:
                if (schedule.Interval is null || schedule.Interval < 1)
                {
                    throw new ArgumentException("Hourly schedule requires interval >= 1.", parameterName);
                }

                break;
            case AutomationScheduleKind.Daily:
                ValidateLocalTime(schedule.LocalTime, parameterName);
                break;
            case AutomationScheduleKind.Weekly:
                ValidateLocalTime(schedule.LocalTime, parameterName);
                if (schedule.DaysOfWeek is not { Count: > 0 })
                {
                    throw new ArgumentException("Weekly schedule requires at least one day of week.", parameterName);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(parameterName, schedule.Kind, "Unsupported schedule kind.");
        }
    }

    private static void ValidateLocalTime(string? localTime, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(localTime))
        {
            throw new ArgumentException("Schedule requires local time in HH:mm format.", parameterName);
        }

        var normalized = localTime.Trim();
        if (!TimeOnly.TryParseExact(normalized, LocalTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException("Schedule requires local time in HH:mm format.", parameterName);
        }
    }
}
