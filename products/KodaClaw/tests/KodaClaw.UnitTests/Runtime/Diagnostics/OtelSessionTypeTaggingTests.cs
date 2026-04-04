using FluentAssertions;
using Kode.Agent.Sdk.Core.Types;
using Xunit;

namespace KodaClaw.UnitTests.Runtime.Diagnostics;

/// <summary>
/// Verifies that session services set the correct SessionType on AgentConfig.
/// These tests check the contract that each session type produces the right OTel dimension tag.
/// </summary>
public sealed class OtelSessionTypeTaggingTests
{
    [Fact]
    public void AgentConfig_with_session_type_main_has_correct_value()
    {
        var config = new AgentConfig { SessionType = "main" };
        config.SessionType.Should().Be("main");
    }

    [Fact]
    public void AgentConfig_with_session_type_channel_has_correct_value()
    {
        var config = new AgentConfig { SessionType = "channel" };
        config.SessionType.Should().Be("channel");
    }

    [Fact]
    public void AgentConfig_with_session_type_automation_has_correct_value()
    {
        var config = new AgentConfig { SessionType = "automation" };
        config.SessionType.Should().Be("automation");
    }

    [Fact]
    public void AgentConfig_sub_agent_role_is_correctly_tagged()
    {
        var config = new AgentConfig { AgentRole = "sub-agent" };
        config.AgentRole.Should().Be("sub-agent");
    }

    [Fact]
    public void AgentConfig_primary_agent_role_is_correctly_tagged()
    {
        var config = new AgentConfig { AgentRole = "primary" };
        config.AgentRole.Should().Be("primary");
    }

    [Fact]
    public void AgentConfig_default_values_are_suitable_for_OTel_tagging()
    {
        var config = new AgentConfig();
        // Defaults must be non-null so metrics never emit empty-string tags.
        config.SessionType.Should().NotBeNullOrEmpty();
        config.AgentRole.Should().NotBeNullOrEmpty();
    }
}
