using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KodaClaw.Contracts;

namespace KodaClaw.Workspace;

public sealed class HeartbeatAutomationCompiler : IHeartbeatAutomationCompiler
{
    internal const string HeartbeatSourcePath = $"{KodaClawWorkspaceLayout.WorkspaceDirectory}/{KodaClawWorkspaceLayout.HeartbeatFile}";

    private static readonly Regex MinutesPattern = new(
        "^every\\s+(\\d+)m$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HourlyPattern = new(
        "^hourly\\s+(\\d+)h$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DailyPattern = new(
        "^daily\\s+(\\d{2}:\\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex WeekdaysPattern = new(
        "^weekdays\\s+(\\d{2}:\\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex WeeklyPattern = new(
        "^weekly\\s+(.+?)\\s+(\\d{2}:\\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public IReadOnlyList<AutomationDefinition> Compile(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new HeartbeatCompilationException("HEARTBEAT markdown is required.");
        }

        var lines = NormalizeNewLines(markdown).Split('\n');
        var sections = ParseSections(lines);
        if (sections.Count == 0)
        {
            throw new HeartbeatCompilationException("At least one automation section is required. Use '## <Title>'.");
        }

        return BuildDefinitions(sections);
    }

    private static string NormalizeNewLines(string markdown)
    {
        return markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static List<SectionDraft> ParseSections(string[] lines)
    {
        var sections = new List<SectionDraft>();
        var lineIndex = 0;
        SectionDraft? currentSection = null;

        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex];
            var trimmed = line.Trim();
            var lineNumber = lineIndex + 1;

            if (trimmed.Length == 0)
            {
                lineIndex++;
                continue;
            }

            if (trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                var title = trimmed[2..].Trim();
                if (title.Length == 0)
                {
                    throw new HeartbeatCompilationException("Automation title is required after '##'.", lineNumber);
                }

                currentSection = new SectionDraft(title);
                sections.Add(currentSection);
                lineIndex++;
                continue;
            }

            if (currentSection is null)
            {
                if (trimmed.StartsWith('#'))
                {
                    lineIndex++;
                    continue;
                }

                throw new HeartbeatCompilationException(
                    "Automation content must be inside a section that starts with '## <Title>'.",
                    lineNumber);
            }

            if (!TryReadTopLevelBullet(line, out var bulletContent))
            {
                throw new HeartbeatCompilationException("Expected a top-level bullet field.", lineNumber);
            }

            if (TryReadFieldValue(bulletContent, "schedule", out var schedule))
            {
                currentSection.ScheduleExpression = EnsureSingleAssignment(
                    currentSection.ScheduleExpression,
                    "schedule",
                    schedule,
                    lineNumber);
                lineIndex++;
                continue;
            }

            if (TryReadFieldValue(bulletContent, "prompt", out var prompt))
            {
                if (string.Equals(prompt, ">", StringComparison.Ordinal))
                {
                    // YAML-style block scalar: collect indented continuation lines until the
                    // next top-level bullet or section header.
                    lineIndex++;
                    var sb = new StringBuilder();
                    while (lineIndex < lines.Length)
                    {
                        var contLine = lines[lineIndex];
                        var contTrimmed = contLine.Trim();
                        if (contTrimmed.StartsWith("##", StringComparison.Ordinal) || IsTopLevelBullet(contLine))
                        {
                            break;
                        }

                        if (contTrimmed.Length > 0)
                        {
                            if (sb.Length > 0)
                            {
                                sb.Append(' ');
                            }

                            sb.Append(contTrimmed);
                        }

                        lineIndex++;
                    }

                    prompt = sb.ToString();
                    if (prompt.Length == 0)
                    {
                        throw new HeartbeatCompilationException("Field 'prompt' block scalar cannot be empty.", lineNumber);
                    }
                }
                else
                {
                    lineIndex++;
                }

                currentSection.Prompt = EnsureSingleAssignment(
                    currentSection.Prompt,
                    "prompt",
                    prompt,
                    lineNumber);
                continue;
            }

            if (TryReadFieldValue(bulletContent, "model", out var modelId))
            {
                currentSection.ModelId = EnsureSingleAssignment(
                    currentSection.ModelId,
                    "model",
                    modelId,
                    lineNumber);
                lineIndex++;
                continue;
            }

            if (TryReadFieldValue(bulletContent, "enabled", out var enabledText))
            {
                currentSection.Enabled = ParseEnabled(enabledText, lineNumber);
                lineIndex++;
                continue;
            }

            if (string.Equals(bulletContent, "inputs:", StringComparison.OrdinalIgnoreCase))
            {
                lineIndex = ParseInputs(lines, lineIndex + 1, currentSection);
                continue;
            }

            if (string.Equals(bulletContent, "channels:", StringComparison.OrdinalIgnoreCase))
            {
                lineIndex = ParseChannels(lines, lineIndex + 1, currentSection);
                continue;
            }

            if (TryReadFieldValue(bulletContent, "delivery-mode", out var deliveryMode))
            {
                currentSection.NotifyMode = ParseNotifyMode(deliveryMode);
                lineIndex++;
                continue;
            }

            throw new HeartbeatCompilationException($"Unsupported field '{bulletContent}'.", lineNumber);
        }

        return sections;
    }

    private static int ParseChannels(string[] lines, int startIndex, SectionDraft section)
    {
        var lineIndex = startIndex;
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex];
            var trimmed = line.Trim();
            var lineNumber = lineIndex + 1;

            if (trimmed.Length == 0)
            {
                lineIndex++;
                continue;
            }

            if (trimmed.StartsWith("##", StringComparison.Ordinal) || IsTopLevelBullet(line))
            {
                break;
            }

            if (!TryReadNestedBullet(line, out var bindingId))
            {
                throw new HeartbeatCompilationException(
                    "Channels must be declared as nested bullets under '- channels:'.",
                    lineNumber);
            }

            section.NotificationChannels.Add(bindingId);
            lineIndex++;
        }

        return lineIndex;
    }

    private static int ParseInputs(string[] lines, int startIndex, SectionDraft section)
    {
        var lineIndex = startIndex;
        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex];
            var trimmed = line.Trim();
            var lineNumber = lineIndex + 1;

            if (trimmed.Length == 0)
            {
                lineIndex++;
                continue;
            }

            if (trimmed.StartsWith("##", StringComparison.Ordinal) || IsTopLevelBullet(line))
            {
                break;
            }

            if (!TryReadNestedBullet(line, out var inputPath))
            {
                throw new HeartbeatCompilationException(
                    "Inputs must be declared as nested bullets under '- inputs:'.",
                    lineNumber);
            }

            section.Inputs.Add(NormalizeInputPath(inputPath, lineNumber));
            lineIndex++;
        }

        return lineIndex;
    }

    private static bool TryReadFieldValue(string bulletContent, string fieldName, out string value)
    {
        var prefix = $"{fieldName}:";
        if (!bulletContent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = string.Empty;
            return false;
        }

        value = bulletContent[prefix.Length..].Trim();
        if (value.Length == 0)
        {
            throw new HeartbeatCompilationException($"Field '{fieldName}' cannot be empty.");
        }

        return true;
    }

    private static string EnsureSingleAssignment(string? currentValue, string fieldName, string newValue, int lineNumber)
    {
        if (currentValue is not null)
        {
            throw new HeartbeatCompilationException($"Field '{fieldName}' can only be declared once.", lineNumber);
        }

        return newValue;
    }

    private static AutomationNotifyMode ParseNotifyMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "auto" => AutomationNotifyMode.Auto,
            "approval" => AutomationNotifyMode.Approval,
            _ => AutomationNotifyMode.None,
        };
    }

    private static bool ParseEnabled(string enabledText, int lineNumber)
    {
        if (bool.TryParse(enabledText, out var enabled))
        {
            return enabled;
        }

        throw new HeartbeatCompilationException("Field 'enabled' must be 'true' or 'false'.", lineNumber);
    }

    private static IReadOnlyList<AutomationDefinition> BuildDefinitions(IReadOnlyList<SectionDraft> sections)
    {
        var definitions = new List<AutomationDefinition>(sections.Count);
        var slugCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var section in sections)
        {
            var title = section.Title;
            if (title.Length == 0)
            {
                throw new HeartbeatCompilationException("Automation title is required.");
            }

            if (string.IsNullOrWhiteSpace(section.ScheduleExpression))
            {
                throw new HeartbeatCompilationException($"Automation '{title}' is missing required 'schedule' field.");
            }

            if (string.IsNullOrWhiteSpace(section.Prompt))
            {
                throw new HeartbeatCompilationException($"Automation '{title}' is missing required 'prompt' field.");
            }

            var schedule = ParseSchedule(section.ScheduleExpression, title);
            var slug = SlugifyTitle(title);
            var count = slugCounts.TryGetValue(slug, out var previousCount) ? previousCount + 1 : 1;
            slugCounts[slug] = count;
            var id = count == 1 ? slug : $"{slug}-{count}";

            definitions.Add(new AutomationDefinition(
                Id: id,
                Title: title,
                Prompt: section.Prompt,
                Source: AutomationDefinitionSource.Heartbeat,
                SourcePath: HeartbeatSourcePath,
                Schedule: schedule,
                Enabled: section.Enabled,
                InputPaths: section.Inputs.ToArray(),
                ModelId: section.ModelId,
                NotificationChannels: section.NotificationChannels.Count > 0 ? section.NotificationChannels.ToArray() : null,
                NotifyMode: section.NotifyMode,
                CreatedAt: DateTimeOffset.UnixEpoch,
                UpdatedAt: DateTimeOffset.UnixEpoch,
                LastRunAt: null,
                NextRunAt: null,
                LastRunStatus: null,
                LastError: null));
        }

        return definitions;
    }

    private static AutomationSchedule ParseSchedule(string expression, string title)
    {
        var normalizedExpression = expression.Trim();
        if (normalizedExpression.Length == 0)
        {
            throw new HeartbeatCompilationException($"Automation '{title}' has an empty schedule expression.");
        }

        var minutesMatch = MinutesPattern.Match(normalizedExpression);
        if (minutesMatch.Success)
        {
            var intervalMinutes = int.Parse(minutesMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            if (intervalMinutes < 5)
            {
                throw new HeartbeatCompilationException(
                    $"Automation '{title}' has invalid minutes interval '{normalizedExpression}': minimum is 5 minutes.");
            }

            return new AutomationSchedule(
                Kind: AutomationScheduleKind.Minutes,
                Interval: intervalMinutes,
                LocalTime: null,
                DaysOfWeek: null);
        }

        var hourlyMatch = HourlyPattern.Match(normalizedExpression);
        if (hourlyMatch.Success)
        {
            var intervalHours = int.Parse(hourlyMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            if (intervalHours <= 0)
            {
                throw new HeartbeatCompilationException(
                    $"Automation '{title}' has invalid hourly interval '{normalizedExpression}'.");
            }

            return new AutomationSchedule(
                Kind: AutomationScheduleKind.Hourly,
                Interval: intervalHours,
                LocalTime: null,
                DaysOfWeek: null);
        }

        var dailyMatch = DailyPattern.Match(normalizedExpression);
        if (dailyMatch.Success)
        {
            var localTime = ParseTimeToken(dailyMatch.Groups[1].Value, title, normalizedExpression);
            return new AutomationSchedule(
                Kind: AutomationScheduleKind.Daily,
                Interval: null,
                LocalTime: localTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                DaysOfWeek: null);
        }

        var weekdaysMatch = WeekdaysPattern.Match(normalizedExpression);
        if (weekdaysMatch.Success)
        {
            var localTime = ParseTimeToken(weekdaysMatch.Groups[1].Value, title, normalizedExpression);
            return new AutomationSchedule(
                Kind: AutomationScheduleKind.Weekly,
                Interval: null,
                LocalTime: localTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                DaysOfWeek:
                [
                    AutomationScheduleDay.Monday,
                    AutomationScheduleDay.Tuesday,
                    AutomationScheduleDay.Wednesday,
                    AutomationScheduleDay.Thursday,
                    AutomationScheduleDay.Friday,
                ]);
        }

        var weeklyMatch = WeeklyPattern.Match(normalizedExpression);
        if (weeklyMatch.Success)
        {
            var dayTokens = weeklyMatch.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (dayTokens.Length == 0)
            {
                throw new HeartbeatCompilationException(
                    $"Automation '{title}' has invalid weekly schedule '{normalizedExpression}'.");
            }

            var days = new List<AutomationScheduleDay>(dayTokens.Length);
            foreach (var token in dayTokens)
            {
                days.Add(ParseDayToken(token, title, normalizedExpression));
            }

            var localTime = ParseTimeToken(weeklyMatch.Groups[2].Value, title, normalizedExpression);
            return new AutomationSchedule(
                Kind: AutomationScheduleKind.Weekly,
                Interval: null,
                LocalTime: localTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                DaysOfWeek: days.ToArray());
        }

        throw new HeartbeatCompilationException(
            $"Automation '{title}' has invalid schedule '{normalizedExpression}'. " +
            "Supported forms: 'every 15m', 'hourly 2h', 'daily 09:00', 'weekdays 09:00', 'weekly mon,wed,fri 18:30'.");
    }

    private static TimeOnly ParseTimeToken(string token, string title, string expression)
    {
        if (TimeOnly.TryParseExact(
            token,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var localTime))
        {
            return localTime;
        }

        throw new HeartbeatCompilationException(
            $"Automation '{title}' has invalid time token in schedule '{expression}'.");
    }

    private static AutomationScheduleDay ParseDayToken(string token, string title, string expression)
    {
        return token.ToLowerInvariant() switch
        {
            "mon" => AutomationScheduleDay.Monday,
            "tue" => AutomationScheduleDay.Tuesday,
            "wed" => AutomationScheduleDay.Wednesday,
            "thu" => AutomationScheduleDay.Thursday,
            "fri" => AutomationScheduleDay.Friday,
            "sat" => AutomationScheduleDay.Saturday,
            "sun" => AutomationScheduleDay.Sunday,
            _ => throw new HeartbeatCompilationException(
                $"Automation '{title}' has invalid weekday token '{token}' in schedule '{expression}'."),
        };
    }

    private static string SlugifyTitle(string title)
    {
        var builder = new StringBuilder(title.Length);
        var lastWasHyphen = false;
        foreach (var character in title.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasHyphen = false;
                continue;
            }

            if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            throw new HeartbeatCompilationException($"Automation title '{title}' cannot be converted into an id slug.");
        }

        return slug;
    }

    private static string NormalizeInputPath(string inputPath, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new HeartbeatCompilationException("Input path cannot be empty.", lineNumber);
        }

        var normalized = inputPath.Trim().Replace('\\', '/');
        if (normalized.StartsWith("~/", StringComparison.Ordinal)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.StartsWith("\\", StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new HeartbeatCompilationException("Input path must be workspace-relative.", lineNumber);
        }

        if (normalized.StartsWith("workspace/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["workspace/".Length..];
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                throw new HeartbeatCompilationException(
                    "Input path cannot traverse outside workspace.",
                    lineNumber);
            }

            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new HeartbeatCompilationException("Input path contains invalid characters.", lineNumber);
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            throw new HeartbeatCompilationException("Input path cannot be empty.", lineNumber);
        }

        return string.Join('/', segments);
    }

    private static bool TryReadTopLevelBullet(string line, out string content)
    {
        if (!IsTopLevelBullet(line))
        {
            content = string.Empty;
            return false;
        }

        content = line.Trim()[2..].Trim();
        if (content.Length == 0)
        {
            throw new HeartbeatCompilationException("Bullet field cannot be empty.");
        }

        return true;
    }

    private static bool IsTopLevelBullet(string line)
    {
        return LeadingWhitespaceLength(line) == 0
            && line.TrimStart().StartsWith("- ", StringComparison.Ordinal);
    }

    private static bool TryReadNestedBullet(string line, out string content)
    {
        var trimmed = line.Trim();
        if (LeadingWhitespaceLength(line) == 0 || !trimmed.StartsWith("- ", StringComparison.Ordinal))
        {
            content = string.Empty;
            return false;
        }

        content = trimmed[2..].Trim();
        if (content.Length == 0)
        {
            throw new HeartbeatCompilationException("Input bullet cannot be empty.");
        }

        return true;
    }

    private static int LeadingWhitespaceLength(string value)
    {
        var index = 0;
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        return index;
    }

    private sealed class SectionDraft(string title)
    {
        public string Title { get; } = title;

        public string? ScheduleExpression { get; set; }

        public string? Prompt { get; set; }

        public string? ModelId { get; set; }

        public bool Enabled { get; set; } = true;

        public List<string> Inputs { get; } = [];

        public List<string> NotificationChannels { get; } = [];

        public AutomationNotifyMode NotifyMode { get; set; } = AutomationNotifyMode.None;
    }
}
