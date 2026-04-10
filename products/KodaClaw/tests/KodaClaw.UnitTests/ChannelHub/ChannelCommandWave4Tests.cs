using FluentAssertions;
using Kode.Agent.Sdk.Core.Types;
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

    // ── FilterAndTrimMessages（Context Fork 消息过滤）─────────────────────────────

    [Fact]
    public void FilterAndTrimMessages_keeps_text_content_only()
    {
        var messages = new List<Message>
        {
            Message.User("你好"),
            Message.Assistant("好的，我来帮你"),
            // User message with only ToolResultContent — should be dropped
            new Message
            {
                Role = MessageRole.User,
                Content = [new ToolResultContent { ToolUseId = "call-1", Content = "result" }]
            },
            // Assistant message with mixed content — only TextContent kept
            new Message
            {
                Role = MessageRole.Assistant,
                Content =
                [
                    new TextContent { Text = "找到问题了" },
                    new ToolUseContent { Id = "call-2", Name = "bash_run", Input = new { } }
                ]
            },
        };

        var result = ChannelCommandDispatcher.FilterAndTrimMessages(messages);

        result.Should().HaveCount(3);
        result[0].Role.Should().Be(MessageRole.User);
        result[0].Content.Should().AllBeOfType<TextContent>();
        result[1].Role.Should().Be(MessageRole.Assistant);
        result[1].Content.Should().AllBeOfType<TextContent>();
        // The mixed-content assistant message keeps only TextContent
        result[2].Role.Should().Be(MessageRole.Assistant);
        result[2].Content.Should().ContainSingle().Which.Should().BeOfType<TextContent>();
    }

    [Fact]
    public void FilterAndTrimMessages_skips_system_messages()
    {
        var messages = new List<Message>
        {
            new Message { Role = MessageRole.System, Content = [new TextContent { Text = "system" }] },
            Message.User("问题"),
        };

        var result = ChannelCommandDispatcher.FilterAndTrimMessages(messages);

        result.Should().ContainSingle().Which.Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public void FilterAndTrimMessages_trims_to_last_40_messages()
    {
        // Build 60 alternating User/Assistant text messages
        var messages = Enumerable.Range(0, 60)
            .Select(i => i % 2 == 0
                ? Message.User($"用户消息 {i}")
                : Message.Assistant($"助手回复 {i}"))
            .ToList();

        var result = ChannelCommandDispatcher.FilterAndTrimMessages(messages);

        result.Should().HaveCount(40);
        // Should be the last 40 (index 20-59)
        ((TextContent)result[0].Content[0]).Text.Should().Be("用户消息 20");
        ((TextContent)result[39].Content[0]).Text.Should().Be("助手回复 59");
    }

    [Fact]
    public void FilterAndTrimMessages_returns_empty_when_all_messages_are_tool_only()
    {
        var messages = new List<Message>
        {
            new Message
            {
                Role = MessageRole.User,
                Content = [new ToolResultContent { ToolUseId = "call-1", Content = "result" }]
            },
            new Message
            {
                Role = MessageRole.Assistant,
                Content = [new ToolUseContent { Id = "call-1", Name = "bash_run", Input = new { } }]
            },
        };

        var result = ChannelCommandDispatcher.FilterAndTrimMessages(messages);

        result.Should().BeEmpty();
    }

    // ── EphemeralAgentStore ───────────────────────────────────────────────────────

    [Fact]
    public async Task EphemeralAgentStore_always_returns_false_for_exists()
    {
        var store = new EphemeralAgentStore();
        var exists = await store.ExistsAsync("any-id");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task EphemeralAgentStore_always_returns_null_info()
    {
        var store = new EphemeralAgentStore();
        var info = await store.LoadInfoAsync("test-agent");
        info.Should().BeNull();
    }

    [Fact]
    public async Task EphemeralAgentStore_always_returns_empty_messages()
    {
        var store = new EphemeralAgentStore();
        var messages = await store.LoadMessagesAsync("any-id");
        messages.Should().BeEmpty();
    }

    // ── FormatMessagesAsText ──────────────────────────────────────────────────────

    [Fact]
    public void FormatMessagesAsText_formats_user_and_assistant_roles()
    {
        var messages = new List<Message>
        {
            Message.User("你好"),
            Message.Assistant("你好，有什么需要帮助的？"),
        };

        var result = ChannelCommandDispatcher.FormatMessagesAsText(messages);

        result.Should().Contain("[用户] 你好");
        result.Should().Contain("[助手] 你好，有什么需要帮助的？");
    }

    [Fact]
    public void FormatMessagesAsText_returns_empty_string_for_empty_list()
    {
        var result = ChannelCommandDispatcher.FormatMessagesAsText([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FormatMessagesAsText_skips_empty_text_content()
    {
        var messages = new List<Message>
        {
            new Message
            {
                Role = MessageRole.User,
                Content = [new TextContent { Text = "   " }]
            },
            Message.Assistant("有效回复"),
        };

        var result = ChannelCommandDispatcher.FormatMessagesAsText(messages);

        result.Should().NotContain("[用户]");
        result.Should().Contain("[助手] 有效回复");
    }
}
