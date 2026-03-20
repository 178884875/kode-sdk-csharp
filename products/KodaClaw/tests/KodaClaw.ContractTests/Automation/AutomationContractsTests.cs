using System.Text.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using Xunit;

namespace KodaClaw.ContractTests.Automation;

public sealed class AutomationContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Automation_definition_should_json_round_trip()
    {
        var payload = new AutomationDefinition(
            Id: "daily-inbox-digest",
            Title: "Daily Inbox Digest",
            Prompt: "Review unresolved inbox items.",
            Source: AutomationDefinitionSource.Heartbeat,
            SourcePath: "workspace/HEARTBEAT.md",
            Schedule: new AutomationSchedule(
                Kind: AutomationScheduleKind.Weekly,
                Interval: null,
                LocalTime: "09:00",
                DaysOfWeek:
                [
                    AutomationScheduleDay.Monday,
                    AutomationScheduleDay.Tuesday,
                    AutomationScheduleDay.Wednesday,
                    AutomationScheduleDay.Thursday,
                    AutomationScheduleDay.Friday,
                ]),
            Enabled: true,
            InputPaths: ["inbox", "tasks"],
            CreatedAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero),
            LastRunAt: null,
            NextRunAt: null,
            LastRunStatus: null,
            LastError: null);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<AutomationDefinition>(json, JsonOptions);

        json.Should().Contain("\"source\":\"Heartbeat\"");
        json.Should().Contain("\"kind\":\"Weekly\"");
        json.Should().Contain("\"daysOfWeek\":[\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\"]");
        roundTrip.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Automation_run_record_should_json_round_trip()
    {
        var payload = new AutomationRunRecord(
            RunId: "run-001",
            AutomationId: "daily-inbox-digest",
            Status: AutomationRunStatus.Succeeded,
            Trigger: "heartbeat",
            Attempt: 1,
            SessionId: "auto-20260318090000-daily-abcd1234",
            StartedAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero),
            CompletedAt: new DateTimeOffset(2026, 3, 18, 9, 1, 0, TimeSpan.Zero),
            Summary: "Digest posted to inbox.",
            ErrorMessage: null);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<AutomationRunRecord>(json, JsonOptions);

        json.Should().Contain("\"status\":\"Succeeded\"");
        roundTrip.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Automation_query_responses_and_patch_request_should_round_trip()
    {
        var definition = new AutomationDefinition(
            Id: "daily-inbox-digest",
            Title: "Daily Inbox Digest",
            Prompt: "Review unresolved inbox items.",
            Source: AutomationDefinitionSource.Heartbeat,
            SourcePath: "workspace/HEARTBEAT.md",
            Schedule: new AutomationSchedule(
                Kind: AutomationScheduleKind.Daily,
                Interval: null,
                LocalTime: "09:00",
                DaysOfWeek: null),
            Enabled: true,
            InputPaths: ["workspace/inbox"],
            CreatedAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 3, 18, 9, 30, 0, TimeSpan.Zero),
            LastRunAt: null,
            NextRunAt: null,
            LastRunStatus: null,
            LastError: null);
        var run = new AutomationRunRecord(
            RunId: "run-001",
            AutomationId: definition.Id,
            Status: AutomationRunStatus.Running,
            Trigger: "automation.scheduler",
            Attempt: 1,
            SessionId: "auto-session-001",
            StartedAt: new DateTimeOffset(2026, 3, 18, 9, 31, 0, TimeSpan.Zero),
            CompletedAt: null,
            Summary: null,
            ErrorMessage: null);
        var definitionsResponse = new AutomationDefinitionsQueryResponse([definition]);
        var runsResponse = new AutomationRunsQueryResponse([run]);
        var patchRequest = new UpdateAutomationDefinitionRequest(Enabled: false);

        var definitionsJson = JsonSerializer.Serialize(definitionsResponse, JsonOptions);
        var runsJson = JsonSerializer.Serialize(runsResponse, JsonOptions);
        var patchJson = JsonSerializer.Serialize(patchRequest, JsonOptions);

        definitionsJson.Should().Contain("\"items\"");
        definitionsJson.Should().Contain("\"source\":\"Heartbeat\"");
        runsJson.Should().Contain("\"status\":\"Running\"");
        patchJson.Should().Contain("\"enabled\":false");

        JsonSerializer.Deserialize<AutomationDefinitionsQueryResponse>(definitionsJson, JsonOptions)
            .Should()
            .BeEquivalentTo(definitionsResponse);
        JsonSerializer.Deserialize<AutomationRunsQueryResponse>(runsJson, JsonOptions)
            .Should()
            .BeEquivalentTo(runsResponse);
        JsonSerializer.Deserialize<UpdateAutomationDefinitionRequest>(patchJson, JsonOptions)
            .Should()
            .Be(patchRequest);
    }
}
