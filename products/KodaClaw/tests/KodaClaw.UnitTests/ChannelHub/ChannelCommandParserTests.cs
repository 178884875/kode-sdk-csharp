using FluentAssertions;
using KodaClaw.ChannelHub.Commands;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

/// <summary>
/// KC-CMD-W1: Unit tests for ChannelCommandParser.
/// </summary>
public sealed class ChannelCommandParserTests
{
    // ── Null / empty ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_null_returns_no_command()
    {
        var result = ChannelCommandParser.Parse(null);
        result.ControlKind.Should().BeNull();
        result.Directives.Should().BeEmpty();
        result.CleanedText.Should().Be("");
    }

    [Fact]
    public void Parse_empty_returns_no_command()
    {
        var result = ChannelCommandParser.Parse("");
        result.ControlKind.Should().BeNull();
        result.Directives.Should().BeEmpty();
        result.CleanedText.Should().Be("");
    }

    // ── Plain text ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("hello world")]
    [InlineData("帮我查天气")]
    [InlineData("no slash here")]
    public void Parse_plain_text_returns_no_command(string text)
    {
        var result = ChannelCommandParser.Parse(text);
        result.ControlKind.Should().BeNull();
        result.Directives.Should().BeEmpty();
        result.CleanedText.Should().Be(text);
    }

    // ── Control commands ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/new", ChannelControlCommandKind.NewSession)]
    [InlineData("/clear", ChannelControlCommandKind.NewSession)]
    [InlineData("/reset", ChannelControlCommandKind.NewSession)]
    [InlineData("/status", ChannelControlCommandKind.Status)]
    [InlineData("/s", ChannelControlCommandKind.Status)]
    [InlineData("/stop", ChannelControlCommandKind.Stop)]
    [InlineData("/help", ChannelControlCommandKind.Help)]
    [InlineData("/commands", ChannelControlCommandKind.Help)]
    [InlineData("/?", ChannelControlCommandKind.Help)]
    [InlineData("/compact", ChannelControlCommandKind.Compact)]
    [InlineData("/tools", ChannelControlCommandKind.Tools)]
    [InlineData("/whoami", ChannelControlCommandKind.WhoAmI)]
    [InlineData("/me", ChannelControlCommandKind.WhoAmI)]
    [InlineData("/btw", ChannelControlCommandKind.SideQuestion)]
    public void Parse_control_commands_returns_correct_kind(string input, ChannelControlCommandKind expected)
    {
        var result = ChannelCommandParser.Parse(input);
        result.ControlKind.Should().Be(expected);
        result.Directives.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/NEW", ChannelControlCommandKind.NewSession)]
    [InlineData("/Status", ChannelControlCommandKind.Status)]
    [InlineData("/STOP", ChannelControlCommandKind.Stop)]
    public void Parse_control_commands_are_case_insensitive(string input, ChannelControlCommandKind expected)
    {
        var result = ChannelCommandParser.Parse(input);
        result.ControlKind.Should().Be(expected);
    }

    [Fact]
    public void Parse_control_command_with_trailing_arg()
    {
        var result = ChannelCommandParser.Parse("/btw what is 2+2?");
        result.ControlKind.Should().Be(ChannelControlCommandKind.SideQuestion);
        result.ControlArg.Should().Be("what is 2+2?");
    }

    [Fact]
    public void Parse_control_command_no_arg_has_null_ControlArg()
    {
        var result = ChannelCommandParser.Parse("/stop");
        result.ControlKind.Should().Be(ChannelControlCommandKind.Stop);
        result.ControlArg.Should().BeNull();
    }

    // ── Unrecognized commands ────────────────────────────────────────────────────

    [Theory]
    [InlineData("/unknown")]
    [InlineData("/newer")]
    [InlineData("/start")]
    public void Parse_unrecognized_slash_command_returns_no_command(string input)
    {
        var result = ChannelCommandParser.Parse(input);
        result.ControlKind.Should().BeNull();
        result.Directives.Should().BeEmpty();
        result.CleanedText.Should().Be(input);
    }

    // ── Directive modifiers ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_think_directive_sets_directive_and_cleaned_text()
    {
        var result = ChannelCommandParser.Parse("/think 分析一下代码");
        result.ControlKind.Should().BeNull();
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Think);
        result.CleanedText.Should().Be("分析一下代码");
    }

    [Fact]
    public void Parse_think_directive_with_no_text_has_empty_cleaned_text()
    {
        var result = ChannelCommandParser.Parse("/think");
        result.ControlKind.Should().BeNull();
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Think);
        result.CleanedText.Should().Be("");
    }

    [Fact]
    public void Parse_stream_directive_sets_directive()
    {
        var result = ChannelCommandParser.Parse("/stream 跑一段代码");
        result.ControlKind.Should().BeNull();
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Stream);
        result.CleanedText.Should().Be("跑一段代码");
    }

    [Fact]
    public void Parse_quiet_directive_sets_directive()
    {
        var result = ChannelCommandParser.Parse("/quiet 帮我写一首诗");
        result.ControlKind.Should().BeNull();
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Quiet);
        result.CleanedText.Should().Be("帮我写一首诗");
    }

    [Fact]
    public void Parse_focus_directive_sets_directiveArg_and_cleaned_text()
    {
        var result = ChannelCommandParser.Parse("/focus 代码质量 帮我审查这段代码");
        result.ControlKind.Should().BeNull();
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Focus);
        result.DirectiveArg.Should().Be("代码质量");
        result.CleanedText.Should().Be("帮我审查这段代码");
    }

    [Fact]
    public void Parse_focus_with_only_topic_has_empty_cleaned_text()
    {
        var result = ChannelCommandParser.Parse("/focus topic");
        result.Directives.Should().ContainSingle().Which.Should().Be(ChannelDirectiveKind.Focus);
        result.DirectiveArg.Should().Be("topic");
        result.CleanedText.Should().Be("");
    }

    [Fact]
    public void Parse_chained_directives_merges_both()
    {
        var result = ChannelCommandParser.Parse("/think /stream 写点东西");
        result.ControlKind.Should().BeNull();
        result.Directives.Should().HaveCount(2);
        result.Directives.Should().Contain(ChannelDirectiveKind.Think);
        result.Directives.Should().Contain(ChannelDirectiveKind.Stream);
        result.CleanedText.Should().Be("写点东西");
    }

    // ── Leading/trailing whitespace ──────────────────────────────────────────────

    [Fact]
    public void Parse_with_leading_whitespace_still_parses()
    {
        var result = ChannelCommandParser.Parse("  /new  ");
        result.ControlKind.Should().Be(ChannelControlCommandKind.NewSession);
    }
}
