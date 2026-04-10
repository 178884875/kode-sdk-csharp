using FluentAssertions;
using KodaClaw.ChannelHub.Commands;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

/// <summary>
/// KC-CMD-W4: Tests for /focus directive and /btw side-question.
/// </summary>
public sealed class ChannelCommandWave4Tests
{
    // ── Parser: /focus directive ─────────────────────────────────────────────────

    [Fact]
    public void Parse_focus_with_topic_and_body()
    {
        var result = ChannelCommandParser.Parse("/focus 代码质量 审查这段代码");
        result.ControlKind.Should().BeNull();
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Focus);
        result.DirectiveArg.Should().Be("代码质量");
        result.CleanedText.Should().Be("审查这段代码");
    }

    [Fact]
    public void Parse_focus_with_only_topic_has_empty_cleaned_text()
    {
        var result = ChannelCommandParser.Parse("/focus security");
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Focus);
        result.DirectiveArg.Should().Be("security");
        result.CleanedText.Should().Be("");
    }

    [Fact]
    public void Parse_focus_with_no_topic_has_null_DirectiveArg()
    {
        var result = ChannelCommandParser.Parse("/focus");
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Focus);
        result.DirectiveArg.Should().BeNull();
        result.CleanedText.Should().Be("");
    }

    // ── Parser: /btw control command ────────────────────────────────────────────

    [Fact]
    public void Parse_btw_with_question()
    {
        var result = ChannelCommandParser.Parse("/btw 2+2 等于多少?");
        result.ControlKind.Should().Be(ChannelControlCommandKind.SideQuestion);
        result.ControlArg.Should().Be("2+2 等于多少?");
        result.Directives.Should().BeEmpty();
    }

    [Fact]
    public void Parse_btw_without_question_has_null_ControlArg()
    {
        var result = ChannelCommandParser.Parse("/btw");
        result.ControlKind.Should().Be(ChannelControlCommandKind.SideQuestion);
        result.ControlArg.Should().BeNull();
    }

    // ── ChannelTurnContext: Focus directive ──────────────────────────────────────

    [Fact]
    public void FromDirectives_focus_sets_FocusConstraint()
    {
        var parsed = new ParsedChannelCommand(null, null, [ChannelDirectiveKind.Focus], "性能优化", "优化这段代码");
        var ctx = ChannelTurnContext.FromDirectives(parsed);

        ctx.FocusConstraint.Should().Be("性能优化");
        ctx.EnableThinking.Should().BeNull();
        ctx.EnableProgressStreamingOverride.Should().BeNull();
    }

    [Fact]
    public void FromDirectives_think_and_focus_combined()
    {
        var parsed = new ParsedChannelCommand(
            null, null,
            [ChannelDirectiveKind.Think, ChannelDirectiveKind.Focus],
            "深度分析",
            "分析这个算法");
        var ctx = ChannelTurnContext.FromDirectives(parsed);

        ctx.EnableThinking.Should().BeTrue();
        ctx.ThinkingBudget.Should().Be(8000);
        ctx.FocusConstraint.Should().Be("深度分析");
    }
}
