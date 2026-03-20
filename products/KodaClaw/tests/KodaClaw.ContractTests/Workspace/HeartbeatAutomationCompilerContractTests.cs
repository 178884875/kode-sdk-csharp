using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.Workspace;
using Xunit;

namespace KodaClaw.ContractTests.Workspace;

public sealed class HeartbeatAutomationCompilerContractTests
{
    private readonly HeartbeatAutomationCompiler _compiler = new();

    [Fact]
    public void Compile_should_support_all_frozen_schedule_grammars()
    {
        var markdown = """
# Heartbeat

## Hourly Patrol
- schedule: hourly 2h
- prompt: Scan recent workspace updates.
- enabled: true

## Daily Digest
- schedule: daily 09:00
- prompt: Summarize daily status.

## Weekday Reminder
- schedule: weekdays 09:00
- prompt: Plan weekday priorities.

## Weekly Ops Review
- schedule: weekly mon,wed,fri 18:30
- prompt: Audit ops checklist.
""";

        var definitions = _compiler.Compile(markdown);

        definitions.Should().HaveCount(4);
        definitions[0].Schedule.Should().BeEquivalentTo(new AutomationSchedule(
            Kind: AutomationScheduleKind.Hourly,
            Interval: 2,
            LocalTime: null,
            DaysOfWeek: null));
        definitions[1].Schedule.Should().BeEquivalentTo(new AutomationSchedule(
            Kind: AutomationScheduleKind.Daily,
            Interval: null,
            LocalTime: "09:00",
            DaysOfWeek: null));
        definitions[2].Schedule.Should().BeEquivalentTo(new AutomationSchedule(
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
            ]));
        definitions[3].Schedule.Should().BeEquivalentTo(new AutomationSchedule(
            Kind: AutomationScheduleKind.Weekly,
            Interval: null,
            LocalTime: "18:30",
            DaysOfWeek:
            [
                AutomationScheduleDay.Monday,
                AutomationScheduleDay.Wednesday,
                AutomationScheduleDay.Friday,
            ]));

        definitions.Should().OnlyContain(x => x.Source == AutomationDefinitionSource.Heartbeat);
        definitions.Should().OnlyContain(x => x.SourcePath == "workspace/HEARTBEAT.md");
        definitions.Should().OnlyContain(x => x.CreatedAt == DateTimeOffset.UnixEpoch);
        definitions.Should().OnlyContain(x => x.UpdatedAt == DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Compile_should_generate_deterministic_ids_and_deduplicate_duplicate_titles()
    {
        var markdown = """
## Daily Check
- schedule: daily 09:00
- prompt: First run.

## Daily Check
- schedule: daily 10:00
- prompt: Second run.
""";

        var definitions = _compiler.Compile(markdown);

        definitions.Select(x => x.Id).Should().Equal("daily-check", "daily-check-2");
    }

    [Fact]
    public void Compile_should_parse_disabled_flag()
    {
        var markdown = """
## Disabled Automation
- schedule: daily 09:00
- prompt: Keep disabled by default.
- enabled: false
""";

        var definitions = _compiler.Compile(markdown);

        definitions.Should().ContainSingle();
        definitions[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Compile_should_normalize_workspace_relative_inputs()
    {
        var markdown = """
## Input Normalization
- schedule: daily 09:00
- prompt: Normalize paths.
- inputs:
  - .\tasks\open.md
  - workspace/inbox
  - ./memory/facts
""";

        var definitions = _compiler.Compile(markdown);

        definitions[0].InputPaths.Should().Equal("tasks/open.md", "inbox", "memory/facts");
    }

    [Fact]
    public void Compile_should_reject_input_path_traversal()
    {
        var markdown = """
## Path Traversal
- schedule: daily 09:00
- prompt: Should fail.
- inputs:
  - ../secrets.txt
""";

        var action = () => _compiler.Compile(markdown);

        action.Should()
            .Throw<HeartbeatCompilationException>()
            .WithMessage("*traverse outside workspace*");
    }

    [Fact]
    public void Compile_should_throw_for_invalid_markdown()
    {
        var markdown = """
## Invalid Markdown
- schedule: daily 09:00
prompt: missing bullet marker
""";

        var action = () => _compiler.Compile(markdown);

        action.Should()
            .Throw<HeartbeatCompilationException>()
            .WithMessage("*Expected a top-level bullet field*");
    }

    [Fact]
    public void Compile_should_throw_for_invalid_schedule_expression()
    {
        var markdown = """
## Invalid Schedule
- schedule: daily 9am
- prompt: Should fail.
""";

        var action = () => _compiler.Compile(markdown);

        action.Should()
            .Throw<HeartbeatCompilationException>()
            .WithMessage("*invalid schedule*");
    }

    [Fact]
    public void Compile_should_throw_for_missing_required_fields()
    {
        var missingScheduleMarkdown = """
## Missing Schedule
- prompt: no schedule.
""";
        var missingPromptMarkdown = """
## Missing Prompt
- schedule: daily 09:00
""";

        var missingSchedule = () => _compiler.Compile(missingScheduleMarkdown);
        var missingPrompt = () => _compiler.Compile(missingPromptMarkdown);

        missingSchedule.Should()
            .Throw<HeartbeatCompilationException>()
            .WithMessage("*missing required 'schedule'*");
        missingPrompt.Should()
            .Throw<HeartbeatCompilationException>()
            .WithMessage("*missing required 'prompt'*");
    }

    [Fact]
    public void Compile_should_throw_for_missing_title()
    {
        var markdown = """
##    
- schedule: daily 09:00
- prompt: missing title.
""";

        var action = () => _compiler.Compile(markdown);

        action.Should()
            .Throw<HeartbeatCompilationException>()
            .WithMessage("*title is required*");
    }
}
