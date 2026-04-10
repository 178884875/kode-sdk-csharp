using FluentAssertions;
using KodaClaw.ChannelHub.Commands;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

/// <summary>
/// KC-5004 / KC-CMD-W1: Session reset commands (/new, /clear, /reset) intercept logic,
/// now routed through ChannelCommandParser.
/// </summary>
public sealed class SessionResetCommandTests
{
    [Theory]
    [InlineData("/new")]
    [InlineData("/clear")]
    [InlineData("/reset")]
    public void Known_reset_commands_should_be_recognized(string command)
    {
        var result = ChannelCommandParser.Parse(command);
        result.ControlKind.Should().Be(ChannelControlCommandKind.NewSession,
            because: $"'{command}' is a known session reset command");
    }

    [Theory]
    [InlineData("/NEW")]
    [InlineData("/Clear")]
    [InlineData("/RESET")]
    public void Reset_commands_should_be_case_insensitive(string command)
    {
        var result = ChannelCommandParser.Parse(command);
        result.ControlKind.Should().Be(ChannelControlCommandKind.NewSession,
            because: "command matching must be case-insensitive for mobile keyboards");
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("帮我查一下天气")]
    [InlineData("/newer")]       // prefix match should NOT trigger
    public void Non_reset_messages_without_slash_commands_should_have_no_control_kind(string text)
    {
        var result = ChannelCommandParser.Parse(text);
        result.ControlKind.Should().BeNull(
            because: $"'{text}' is not a session reset command");
    }

    [Theory]
    [InlineData("")]
    public void Empty_text_should_have_no_control_kind(string text)
    {
        var result = ChannelCommandParser.Parse(text);
        result.ControlKind.Should().BeNull();
    }

    [Fact]
    public void Reset_with_trailing_content_should_still_parse_as_new_session_with_arg()
    {
        // "/new session" still parses as NewSession but ControlArg = "session"
        var result = ChannelCommandParser.Parse("/new session");
        result.ControlKind.Should().Be(ChannelControlCommandKind.NewSession);
        result.ControlArg.Should().Be("session");
    }
}
