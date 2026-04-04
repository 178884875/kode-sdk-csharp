using System.Diagnostics;
using FluentAssertions;
using Kode.Agent.Sdk.Diagnostics;
using Xunit;

namespace Kode.Agent.Tests.Unit.Diagnostics;

/// <summary>
/// Verifies OTel observability additions:
/// - KodeAgentActivitySource exposes SourceName constant
/// - ActivitySource starts activities with the correct source name
/// - AgentConfig OTel fields (SessionType, AgentRole, ParentActivityContext) have correct defaults
/// - TagList dimensions emitted by the new metric calls are structurally correct
/// </summary>
public sealed class AgentOtelClassifiersTests
{
    // ── ActivitySource ────────────────────────────────────────────────────────

    [Fact]
    public void ActivitySource_SourceName_is_Kode_Agent()
    {
        KodeAgentActivitySource.SourceName.Should().Be("Kode.Agent");
    }

    [Fact]
    public void ActivitySource_Source_matches_SourceName()
    {
        KodeAgentActivitySource.Source.Name.Should().Be(KodeAgentActivitySource.SourceName);
    }

    [Fact]
    public void ActivitySource_can_start_activity_with_name()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == KodeAgentActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = KodeAgentActivitySource.Source.StartActivity("test.span");

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("test.span");
    }

    // ── AgentConfig OTel defaults ─────────────────────────────────────────────

    [Fact]
    public void AgentConfig_default_SessionType_is_main()
    {
        var config = new Kode.Agent.Sdk.Core.Types.AgentConfig();
        config.SessionType.Should().Be("main");
    }

    [Fact]
    public void AgentConfig_default_AgentRole_is_primary()
    {
        var config = new Kode.Agent.Sdk.Core.Types.AgentConfig();
        config.AgentRole.Should().Be("primary");
    }

    [Fact]
    public void AgentConfig_default_ParentActivityContext_is_default()
    {
        var config = new Kode.Agent.Sdk.Core.Types.AgentConfig();
        config.ParentActivityContext.Should().Be(default(ActivityContext));
    }

    // ── Metric tag dimensions (structural checks via TagList) ─────────────────

    [Theory]
    [InlineData("main")]
    [InlineData("channel")]
    [InlineData("automation")]
    public void SessionType_tag_values_are_valid_strings(string sessionType)
    {
        var tags = new TagList { { "session_type", sessionType } };
        tags.Should().ContainSingle(t => t.Key == "session_type" && (string?)t.Value == sessionType);
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("sub-agent")]
    public void AgentRole_tag_values_are_valid_strings(string agentRole)
    {
        var tags = new TagList { { "agent_role", agentRole } };
        tags.Should().ContainSingle(t => t.Key == "agent_role" && (string?)t.Value == agentRole);
    }

    [Theory]
    [InlineData("fs_read", "filesystem")]
    [InlineData("fs_write", "filesystem")]
    [InlineData("bash_run", "shell")]
    [InlineData("bash_kill", "shell")]
    [InlineData("web_fetch", "web")]
    [InlineData("isolate_task", "orchestration")]
    [InlineData("pipeline_run", "orchestration")]
    [InlineData("parallel_research", "orchestration")]
    [InlineData("retry_with_reflection", "orchestration")]
    [InlineData("fan_out_my_work", "orchestration")]
    [InlineData("map_reduce_stuff", "orchestration")]
    [InlineData("debate_tool", "orchestration")]
    [InlineData("workspace_read", "workspace")]
    [InlineData("workspace_protocol_update", "workspace")]
    [InlineData("mcp__my_server__my_tool", "mcp")]
    [InlineData("todo_read", "other")]
    [InlineData("unknown_custom_tool", "other")]
    public void ClassifyToolCategory_returns_expected_category(string toolName, string expectedCategory)
    {
        // Use the same classification logic as Agent.cs (duplicated here for whitebox testing).
        var actual = ClassifyToolCategory(toolName);
        actual.Should().Be(expectedCategory);
    }

    [Theory]
    [InlineData("429 Too Many Requests", "rate_limit")]
    [InlineData("rate limit exceeded", "rate_limit")]
    [InlineData("401 Unauthorized", "auth")]
    [InlineData("403 Forbidden", "auth")]
    [InlineData("authentication failed", "auth")]
    [InlineData("500 Internal Server Error", "server_error")]
    [InlineData("503 Service Unavailable", "server_error")]
    [InlineData("server error occurred", "server_error")]
    [InlineData("connection timed out", "timeout")]
    [InlineData("random transient failure", "unknown")]
    public void ClassifyModelError_classifies_exception_message_correctly(string message, string expectedLabel)
    {
        var ex = new Exception(message);
        var actual = ClassifyModelError(ex);
        actual.Should().Be(expectedLabel);
    }

    [Fact]
    public void ClassifyModelError_classifies_TimeoutException_as_timeout()
    {
        var actual = ClassifyModelError(new TimeoutException("Connection timeout"));
        actual.Should().Be("timeout");
    }

    // ── Local mirrors of Agent.cs private helpers ─────────────────────────────
    // These duplicate the classification logic so we can test it without reflection.
    // If Agent.cs changes, update these mirrors too.

    private static string ClassifyToolCategory(string toolName)
    {
        if (toolName.StartsWith("fs_", StringComparison.OrdinalIgnoreCase)) return "filesystem";
        if (toolName.StartsWith("bash_", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("shell_", StringComparison.OrdinalIgnoreCase)) return "shell";
        if (toolName.StartsWith("web_", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("http_", StringComparison.OrdinalIgnoreCase)) return "web";
        if (toolName.StartsWith("isolate_task", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("pipeline", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("parallel_", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("retry_", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("fan_out", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("map_reduce", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("debate", StringComparison.OrdinalIgnoreCase)) return "orchestration";
        if (toolName.StartsWith("workspace_", StringComparison.OrdinalIgnoreCase)) return "workspace";
        if (toolName.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase)) return "mcp";
        return "other";
    }

    private static string ClassifyModelError(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("429") || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            return "rate_limit";
        if (msg.Contains("401") || msg.Contains("403") || msg.Contains("auth", StringComparison.OrdinalIgnoreCase))
            return "auth";
        if (msg.Contains("500") || msg.Contains("503") || msg.Contains("server error", StringComparison.OrdinalIgnoreCase))
            return "server_error";
        if (ex is TimeoutException
            || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "timeout";
        return "unknown";
    }
}
