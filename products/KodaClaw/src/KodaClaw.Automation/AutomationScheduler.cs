using System.Globalization;
using System.Text.Json;
using KodaClaw.Contracts;
using KodaClaw.Runtime;
using Kode.Agent.Sdk.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace KodaClaw.Automation;

public sealed class AutomationScheduler : IAutomationScheduler
{
    private const string SchedulerTrigger = "automation.scheduler";
    private const string SchedulerSource = "automation.scheduler";
    private readonly IAutomationDefinitionRepository _definitionRepository;
    private readonly IAutomationRunRepository _runRepository;
    private readonly IAutomationSessionService _sessionService;
    private readonly IInboxRepository _inboxRepository;
    private readonly IAutomationClock _clock;
    private readonly AutomationSchedulerOptions _options;
    private readonly ILogger<AutomationScheduler>? _logger;

    public AutomationScheduler(
        IAutomationDefinitionRepository definitionRepository,
        IAutomationRunRepository runRepository,
        IAutomationSessionService sessionService,
        IInboxRepository inboxRepository,
        IAutomationClock clock,
        AutomationSchedulerOptions options,
        ILogger<AutomationScheduler>? logger = null)
    {
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _runRepository = runRepository ?? throw new ArgumentNullException(nameof(runRepository));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _inboxRepository = inboxRepository ?? throw new ArgumentNullException(nameof(inboxRepository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        return TickCoreAsync(ignoreEnabledFlag: true, cancellationToken);
    }

    public Task<int> TickAsync(CancellationToken cancellationToken = default)
    {
        return TickCoreAsync(ignoreEnabledFlag: false, cancellationToken);
    }

    private async Task<int> TickCoreAsync(bool ignoreEnabledFlag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ignoreEnabledFlag && !_options.Enabled)
        {
            return 0;
        }

        var now = _clock.UtcNow;
        await RecoverStaleRunsAsync(now, cancellationToken);

        var definitions = await _definitionRepository.ListAsync(
            new AutomationDefinitionQuery(Enabled: true, Source: null, Limit: int.MaxValue),
            cancellationToken);

        var executed = 0;
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsDue(definition, now))
            {
                continue;
            }

            await ExecuteDefinitionAsync(definition, now, cancellationToken);
            executed++;
        }

        return executed;
    }

    private async Task RecoverStaleRunsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var staleQueued = await _runRepository.ListAsync(
            new AutomationRunQuery(AutomationId: null, Status: AutomationRunStatus.Queued, Limit: int.MaxValue),
            cancellationToken);
        var staleRunning = await _runRepository.ListAsync(
            new AutomationRunQuery(AutomationId: null, Status: AutomationRunStatus.Running, Limit: int.MaxValue),
            cancellationToken);

        var staleRuns = staleQueued.Concat(staleRunning).ToArray();
        foreach (var run in staleRuns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var failedRun = run with
            {
                Status = AutomationRunStatus.Failed,
                CompletedAt = now,
                Summary = run.Summary ?? "Scheduler recovered stale run.",
                ErrorMessage = "Run was stale in queued/running state and was marked failed by scheduler recovery.",
            };
            var updated = await _runRepository.UpdateAsync(failedRun, cancellationToken);
            if (!updated)
            {
                continue;
            }

            await UpsertResultInboxItemAsync(failedRun, cancellationToken);
            await MarkDefinitionFailureForRecoveryAsync(failedRun.AutomationId, now, failedRun.ErrorMessage!, cancellationToken);
        }
    }

    private async Task ExecuteDefinitionAsync(
        AutomationDefinition definition,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var runId = $"run-{Guid.NewGuid():N}";
        var attempt = await ComputeNextAttemptAsync(definition.Id, cancellationToken);
        var queuedRun = new AutomationRunRecord(
            RunId: runId,
            AutomationId: definition.Id,
            Status: AutomationRunStatus.Queued,
            Trigger: SchedulerTrigger,
            Attempt: attempt,
            SessionId: null,
            StartedAt: now,
            CompletedAt: null,
            Summary: null,
            ErrorMessage: null);
        await _runRepository.AddAsync(queuedRun, cancellationToken);

        AutomationSessionHandle? handle = null;
        var agentDisposed = false;
        var runState = queuedRun;
        try
        {
            handle = await _sessionService.StartAutomationSessionAsync(definition, cancellationToken);

            runState = runState with
            {
                Status = AutomationRunStatus.Running,
                SessionId = handle.SessionId,
            };
            await _runRepository.UpdateAsync(runState, cancellationToken);

            AgentRunResult runResult;
            try
            {
                runResult = await handle.Agent.RunAsync("Run the scheduled automation now.", cancellationToken);
            }
            finally
            {
                await handle.Agent.DisposeAsync();
                agentDisposed = true;
            }

            if (runResult.Success)
            {
                var successfulRun = runState with
                {
                    Status = AutomationRunStatus.Succeeded,
                    CompletedAt = _clock.UtcNow,
                    Summary = NormalizeText(runResult.Response) ?? "Automation run completed successfully.",
                    ErrorMessage = null,
                };

                await _runRepository.UpdateAsync(successfulRun, cancellationToken);
                await PersistDefinitionSuccessAsync(definition, successfulRun.CompletedAt!.Value, cancellationToken);
                await UpsertResultInboxItemAsync(successfulRun, cancellationToken);
                return;
            }

            var failedRun = runState with
            {
                Status = AutomationRunStatus.Failed,
                CompletedAt = _clock.UtcNow,
                Summary = NormalizeText(runResult.Response) ?? $"Automation run stopped: {runResult.StopReason}.",
                ErrorMessage = $"Run did not complete successfully (stop reason: {runResult.StopReason}).",
            };

            await _runRepository.UpdateAsync(failedRun, cancellationToken);
            await PersistDefinitionFailureAsync(definition, failedRun.CompletedAt!.Value, failedRun.ErrorMessage!, cancellationToken);
            await UpsertResultInboxItemAsync(failedRun, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Automation run failed for definition {AutomationId}.", definition.Id);

            var failedAt = _clock.UtcNow;
            var failedRun = runState with
            {
                Status = AutomationRunStatus.Failed,
                CompletedAt = failedAt,
                Summary = NormalizeText(ex.GetBaseException().Message) ?? "Automation run crashed.",
                ErrorMessage = NormalizeText(ex.ToString()) ?? "Automation run crashed.",
            };

            await _runRepository.UpdateAsync(failedRun, cancellationToken);
            await PersistDefinitionFailureAsync(definition, failedAt, failedRun.ErrorMessage!, cancellationToken);
            await UpsertResultInboxItemAsync(failedRun, cancellationToken);

            if (!agentDisposed && handle is { } danglingHandle)
            {
                await danglingHandle.Agent.DisposeAsync();
            }
        }
    }

    private async Task<int> ComputeNextAttemptAsync(string automationId, CancellationToken cancellationToken)
    {
        var recentRuns = await _runRepository.ListAsync(
            new AutomationRunQuery(AutomationId: automationId, Status: null, Limit: 1),
            cancellationToken);
        if (recentRuns.Count == 0)
        {
            return 1;
        }

        return recentRuns[0].Attempt + 1;
    }

    private async Task PersistDefinitionSuccessAsync(
        AutomationDefinition definition,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var updated = definition with
        {
            UpdatedAt = completedAt,
            LastRunAt = completedAt,
            NextRunAt = ComputeNextRunAt(definition.Schedule, completedAt),
            LastRunStatus = AutomationRunStatus.Succeeded,
            LastError = null,
        };
        await _definitionRepository.UpsertAsync(updated, cancellationToken);
    }

    private async Task PersistDefinitionFailureAsync(
        AutomationDefinition definition,
        DateTimeOffset failedAt,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var updated = definition with
        {
            UpdatedAt = failedAt,
            LastRunStatus = AutomationRunStatus.Failed,
            LastError = NormalizeText(errorMessage),
            NextRunAt = failedAt.Add(_options.FailureRetryDelay),
        };
        await _definitionRepository.UpsertAsync(updated, cancellationToken);
    }

    private async Task MarkDefinitionFailureForRecoveryAsync(
        string automationId,
        DateTimeOffset now,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var definition = await _definitionRepository.GetByIdAsync(automationId, cancellationToken);
        if (definition is null || !definition.Enabled)
        {
            return;
        }

        var updated = definition with
        {
            UpdatedAt = now,
            LastRunStatus = AutomationRunStatus.Failed,
            LastError = NormalizeText(errorMessage),
            NextRunAt = now.Add(_options.FailureRetryDelay),
        };
        await _definitionRepository.UpsertAsync(updated, cancellationToken);
    }

    private async Task UpsertResultInboxItemAsync(AutomationRunRecord run, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var inboxId = $"automation-result-{run.RunId}";
        var existing = await _inboxRepository.GetByIdAsync(inboxId, cancellationToken);
        var summary = NormalizeText(run.Summary) ?? (run.Status == AutomationRunStatus.Succeeded
            ? "Automation run completed."
            : "Automation run failed.");
        var errorMessage = NormalizeText(run.ErrorMessage);
        var payload = JsonSerializer.Serialize(new
        {
            automationId = run.AutomationId,
            runId = run.RunId,
            status = run.Status.ToString(),
            summary,
            errorMessage,
        });

        var item = new InboxItem(
            Id: inboxId,
            Kind: InboxItemKind.AutomationResult,
            Status: InboxItemStatus.Open,
            Title: $"Automation run {(run.Status == AutomationRunStatus.Succeeded ? "succeeded" : "failed")}",
            Summary: summary,
            Source: SchedulerSource,
            CreatedAt: existing?.CreatedAt ?? now,
            UpdatedAt: now,
            RequiresAction: run.Status != AutomationRunStatus.Succeeded,
            Route: $"/automations/{run.AutomationId}",
            SessionId: run.SessionId,
            CorrelationId: existing?.CorrelationId,
            ApprovalId: null,
            PayloadJson: payload,
            ResolvedAt: null);

        await _inboxRepository.UpsertAsync(item, cancellationToken);
    }

    private static bool IsDue(AutomationDefinition definition, DateTimeOffset now)
    {
        return definition.NextRunAt is null || definition.NextRunAt <= now;
    }

    private static DateTimeOffset ComputeNextRunAt(AutomationSchedule schedule, DateTimeOffset now)
    {
        return schedule.Kind switch
        {
            AutomationScheduleKind.Hourly => now.AddHours(schedule.Interval ?? 1),
            AutomationScheduleKind.Daily => ComputeNextDailyRun(schedule, now),
            AutomationScheduleKind.Weekly => ComputeNextWeeklyRun(schedule, now),
            _ => now.AddHours(1),
        };
    }

    private static DateTimeOffset ComputeNextDailyRun(AutomationSchedule schedule, DateTimeOffset now)
    {
        var localTime = ParseLocalTime(schedule.LocalTime);
        var candidate = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            localTime.Hour,
            localTime.Minute,
            0,
            TimeSpan.Zero);
        if (candidate <= now)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    private static DateTimeOffset ComputeNextWeeklyRun(AutomationSchedule schedule, DateTimeOffset now)
    {
        var localTime = ParseLocalTime(schedule.LocalTime);
        var activeDays = (schedule.DaysOfWeek is { Count: > 0 }
            ? schedule.DaysOfWeek
            : [AutomationScheduleDay.Monday]).ToHashSet();

        for (var offset = 0; offset <= 7; offset++)
        {
            var date = now.Date.AddDays(offset);
            var day = ToScheduleDay(date.DayOfWeek);
            if (!activeDays.Contains(day))
            {
                continue;
            }

            var candidate = new DateTimeOffset(
                date.Year,
                date.Month,
                date.Day,
                localTime.Hour,
                localTime.Minute,
                0,
                TimeSpan.Zero);
            if (candidate > now)
            {
                return candidate;
            }
        }

        return now.AddDays(7);
    }

    private static TimeOnly ParseLocalTime(string? localTime)
    {
        var normalized = (localTime ?? "00:00").Trim();
        if (TimeOnly.TryParseExact(
            normalized,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed;
        }

        return new TimeOnly(0, 0);
    }

    private static AutomationScheduleDay ToScheduleDay(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => AutomationScheduleDay.Monday,
            DayOfWeek.Tuesday => AutomationScheduleDay.Tuesday,
            DayOfWeek.Wednesday => AutomationScheduleDay.Wednesday,
            DayOfWeek.Thursday => AutomationScheduleDay.Thursday,
            DayOfWeek.Friday => AutomationScheduleDay.Friday,
            DayOfWeek.Saturday => AutomationScheduleDay.Saturday,
            _ => AutomationScheduleDay.Sunday,
        };
    }

    private static string? NormalizeText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
