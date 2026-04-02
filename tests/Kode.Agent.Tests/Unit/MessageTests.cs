using System.Text.Json;
using Kode.Agent.Sdk.Core.Types;
using Xunit;

namespace Kode.Agent.Tests.Unit;

public class MessageTests
{
    [Fact]
    public void SystemMessage_HasCorrectRole()
    {
        // Act
        var message = Message.System("You are a helpful assistant");
        
        // Assert
        Assert.Equal(MessageRole.System, message.Role);
        Assert.NotNull(message.Content);
        Assert.Single(message.Content);
        Assert.IsType<TextContent>(message.Content[0]);
        Assert.Equal("You are a helpful assistant", ((TextContent)message.Content[0]).Text);
    }

    [Fact]
    public void UserMessage_HasCorrectRole()
    {
        // Act
        var message = Message.User("Hello");
        
        // Assert
        Assert.Equal(MessageRole.User, message.Role);
        Assert.NotNull(message.Content);
        Assert.Single(message.Content);
        Assert.IsType<TextContent>(message.Content[0]);
        Assert.Equal("Hello", ((TextContent)message.Content[0]).Text);
    }

    [Fact]
    public void AssistantMessage_HasCorrectRole()
    {
        // Act
        var message = Message.Assistant("Hi there!");
        
        // Assert
        Assert.Equal(MessageRole.Assistant, message.Role);
        Assert.NotNull(message.Content);
        Assert.Single(message.Content);
        Assert.IsType<TextContent>(message.Content[0]);
        Assert.Equal("Hi there!", ((TextContent)message.Content[0]).Text);
    }

    [Fact]
    public void AssistantMessage_WithMultipleContentBlocks()
    {
        // Arrange
        var text = new TextContent { Text = "I'll help you" };
        var toolUse = new ToolUseContent
        {
            Id = "call-123",
            Name = "test_tool",
            Input = new Dictionary<string, object>()
        };

        // Act
        var message = Message.Assistant(text, toolUse);

        // Assert
        Assert.Equal(MessageRole.Assistant, message.Role);
        Assert.Equal(2, message.Content.Count);
        Assert.IsType<TextContent>(message.Content[0]);
        Assert.IsType<ToolUseContent>(message.Content[1]);
    }

    [Fact]
    public void VideoContent_FromUrl_HasCorrectProperties()
    {
        var video = VideoContent.FromUrl("https://example.com/video.mp4");

        Assert.Equal("video", video.Type);
        Assert.Equal("https://example.com/video.mp4", video.Url);
    }

    [Fact]
    public void FileContent_FromUrl_HasCorrectProperties()
    {
        var file = FileContent.FromUrl("https://cdn.bigmodel.cn/static/demo/demo2.txt");

        Assert.Equal("file", file.Type);
        Assert.Equal("https://cdn.bigmodel.cn/static/demo/demo2.txt", file.Url);
    }

    [Fact]
    public void VideoContent_Roundtrips_PolymorphicJson()
    {
        ContentBlock original = VideoContent.FromUrl("https://example.com/video.mov");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ContentBlock>(json);

        var video = Assert.IsType<VideoContent>(restored);
        Assert.Equal("https://example.com/video.mov", video.Url);
    }

    [Fact]
    public void FileContent_Roundtrips_PolymorphicJson()
    {
        ContentBlock original = FileContent.FromUrl("https://example.com/report.pdf");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ContentBlock>(json);

        var file = Assert.IsType<FileContent>(restored);
        Assert.Equal("https://example.com/report.pdf", file.Url);
    }

    [Fact]
    public void UserMessage_WithVideoAndText_HasCorrectContentBlocks()
    {
        var message = new Message
        {
            Role = MessageRole.User,
            Content =
            [
                new TextContent { Text = "What is in this video?" },
                VideoContent.FromUrl("https://example.com/video.mp4")
            ]
        };

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal(2, message.Content.Count);
        Assert.IsType<TextContent>(message.Content[0]);
        var video = Assert.IsType<VideoContent>(message.Content[1]);
        Assert.Equal("https://example.com/video.mp4", video.Url);
    }

    [Fact]
    public void UserMessage_WithFileAndText_HasCorrectContentBlocks()
    {
        var message = new Message
        {
            Role = MessageRole.User,
            Content =
            [
                new TextContent { Text = "Summarize this file." },
                FileContent.FromUrl("https://cdn.bigmodel.cn/static/demo/demo2.txt")
            ]
        };

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal(2, message.Content.Count);
        Assert.IsType<TextContent>(message.Content[0]);
        var file = Assert.IsType<FileContent>(message.Content[1]);
        Assert.Equal("https://cdn.bigmodel.cn/static/demo/demo2.txt", file.Url);
    }
}
