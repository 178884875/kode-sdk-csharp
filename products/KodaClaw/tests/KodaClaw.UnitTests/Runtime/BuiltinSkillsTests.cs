using FluentAssertions;
using KodaClaw.Runtime;
using Xunit;

namespace KodaClaw.UnitTests.Runtime;

/// <summary>
/// L1 unit tests — BuiltinSkills constant values and per-session AutoActivate lists.
/// </summary>
public sealed class BuiltinSkillsTests
{
    [Fact]
    public void Skill_name_constants_have_expected_values()
    {
        BuiltinSkills.KodaWorkspace.Should().Be("koda-workspace");
        BuiltinSkills.KodaMemory.Should().Be("koda-memory");
        BuiltinSkills.KodaChannels.Should().Be("koda-channels");
        BuiltinSkills.KodaAutomation.Should().Be("koda-automation");
        BuiltinSkills.KodaCanvas.Should().Be("koda-canvas");
    }

    [Fact]
    public void ChatAutoActivate_contains_workspace_and_memory()
    {
        BuiltinSkills.ChatAutoActivate.Should().BeEquivalentTo(
            [BuiltinSkills.KodaWorkspace, BuiltinSkills.KodaMemory]);
    }

    [Fact]
    public void ChannelAutoActivate_contains_workspace_and_channels()
    {
        BuiltinSkills.ChannelAutoActivate.Should().BeEquivalentTo(
            [BuiltinSkills.KodaWorkspace, BuiltinSkills.KodaChannels]);
    }

    [Fact]
    public void AutomationAutoActivate_contains_workspace_and_automation()
    {
        BuiltinSkills.AutomationAutoActivate.Should().BeEquivalentTo(
            [BuiltinSkills.KodaWorkspace, BuiltinSkills.KodaAutomation]);
    }

    [Fact]
    public void Canvas_is_not_in_any_AutoActivate_list()
    {
        BuiltinSkills.ChatAutoActivate.Should().NotContain(BuiltinSkills.KodaCanvas);
        BuiltinSkills.ChannelAutoActivate.Should().NotContain(BuiltinSkills.KodaCanvas);
        BuiltinSkills.AutomationAutoActivate.Should().NotContain(BuiltinSkills.KodaCanvas);
    }
}
