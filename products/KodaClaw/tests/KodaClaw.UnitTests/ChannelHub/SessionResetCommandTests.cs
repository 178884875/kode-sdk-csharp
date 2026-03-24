using FluentAssertions;
using KodaClaw.ChannelHub;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

/// <summary>
/// KC-5004: Session reset commands (/new, /clear, /reset) intercept logic.
/// </summary>
public sealed class SessionResetCommandTests
{
    [Theory]
    [InlineData("/new")]
    [InlineData("/clear")]
    [InlineData("/reset")]
    public void Known_reset_commands_should_be_recognized(string command)
    {
        ChannelTurnOrchestrator.IsSessionResetCommand(command).Should().BeTrue(
            because: $"'{command}' is a known session reset command");
    }

    [Theory]
    [InlineData("/NEW")]
    [InlineData("/Clear")]
    [InlineData("/RESET")]
    public void Reset_commands_should_be_case_insensitive(string command)
    {
        ChannelTurnOrchestrator.IsSessionResetCommand(command).Should().BeTrue(
            because: "command matching must be case-insensitive for mobile keyboards");
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("帮我查一下天气")]
    [InlineData("/help")]
    [InlineData("/start")]
    [InlineData("")]
    [InlineData("/newer")]       // prefix match should NOT trigger
    [InlineData("/new session")] // with trailing content should NOT trigger
    public void Non_reset_messages_should_not_be_recognized(string text)
    {
        ChannelTurnOrchestrator.IsSessionResetCommand(text).Should().BeFalse(
            because: $"'{text}' is not a session reset command");
    }
}
