using FluentAssertions;
using KodaClaw.ChannelHub.Commands;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

/// <summary>
/// KC-CMD-W2: Tests for ChannelTurnContext.FromDirectives factory.
/// </summary>
public sealed class ChannelTurnContextTests
{
    [Fact]
    public void Empty_directives_returns_empty_context()
    {
        var parsed = new ParsedChannelCommand(null, null, [], null, "hello");
        var ctx = ChannelTurnContext.FromDirectives(parsed);

        ctx.EnableThinking.Should().BeNull();
        ctx.ThinkingBudget.Should().BeNull();
        ctx.EnableProgressStreamingOverride.Should().BeNull();
        ctx.FocusConstraint.Should().BeNull();
        ctx.PromptPrefix.Should().BeNull();
    }

    [Fact]
    public void Think_directive_sets_thinking_budget()
    {
        var parsed = new ParsedChannelCommand(null, null, [ChannelDirectiveKind.Think], null, "分析代码");
        var ctx = ChannelTurnContext.FromDirectives(parsed);

        ctx.EnableThinking.Should().BeTrue();
        ctx.ThinkingBudget.Should().Be(8000);
        ctx.EnableProgressStreamingOverride.Should().BeNull();
    }

    [Fact]
    public void Stream_directive_enables_streaming()
    {
        var parsed = new ParsedChannelCommand(null, null, [ChannelDirectiveKind.Stream], null, "跑代码");
        var ctx = ChannelTurnContext.FromDirectives(parsed);

        ctx.EnableProgressStreamingOverride.Should().BeTrue();
        ctx.EnableThinking.Should().BeNull();
    }

    [Fact]
    public void Quiet_directive_disables_streaming()
    {
        var parsed = new ParsedChannelCommand(null, null, [ChannelDirectiveKind.Quiet], null, "帮我写首诗");
        var ctx = ChannelTurnContext.FromDirectives(parsed);

        ctx.EnableProgressStreamingOverride.Should().BeFalse();
        ctx.EnableThinking.Should().BeNull();
    }

    [Fact]
    public void Focus_directive_sets_focus_constraint_from_DirectiveArg()
    {
        var parsed = new ParsedChannelCommand(null, null, [ChannelDirectiveKind.Focus], "代码质量", "审查这段代码");
        var ctx = ChannelTurnContext.FromDirectives(parsed);

        ctx.FocusConstraint.Should().Be("代码质量");
        ctx.EnableThinking.Should().BeNull();
        ctx.EnableProgressStreamingOverride.Should().BeNull();
    }

    [Fact]
    public void Think_and_stream_directives_both_applied()
    {
        var parsed = new ParsedChannelCommand(
            null, null,
            [ChannelDirectiveKind.Think, ChannelDirectiveKind.Stream],
            null, "复杂分析");
        var ctx = ChannelTurnContext.FromDirectives(parsed);

        ctx.EnableThinking.Should().BeTrue();
        ctx.ThinkingBudget.Should().Be(8000);
        ctx.EnableProgressStreamingOverride.Should().BeTrue();
    }

    [Fact]
    public void Empty_is_all_null()
    {
        var ctx = ChannelTurnContext.Empty;
        ctx.EnableThinking.Should().BeNull();
        ctx.ThinkingBudget.Should().BeNull();
        ctx.EnableProgressStreamingOverride.Should().BeNull();
        ctx.FocusConstraint.Should().BeNull();
        ctx.PromptPrefix.Should().BeNull();
    }
}
