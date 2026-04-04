using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class ContextDistillToolTests
{
    private static ContextDistillTool CreateTool(string modelId = "test-model")
    {
        return new ContextDistillTool(
            new Mock<IModelProvider>().Object, modelId,
            new ToolRegistry(), new Mock<ISandboxFactory>().Object);
    }

    private static ToolContext CreateContext() => new()
    {
        AgentId = "test-parent", CallId = "test-call",
        Sandbox = new Mock<ISandbox>().Object,
        SandboxOptions = new SandboxOptions { WorkingDirectory = "/tmp", EnforceBoundary = false },
    };

    [Fact] public void Name_Is_context_distill() => CreateTool().Name.Should().Be("context_distill");

    [Fact] public void Attributes_Are_ReadOnly() => CreateTool().Attributes.ReadOnly.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var result = await CreateTool(modelId).ExecuteAsync(
            new ContextDistillArgs { Content = "text", FocusQuestion = "what?" }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Content_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new ContextDistillArgs { Content = "   ", FocusQuestion = "what?" }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Content");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_FocusQuestion_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new ContextDistillArgs { Content = "some content", FocusQuestion = "" }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("FocusQuestion");
    }

    [Fact]
    public void InputSchema_Contains_content_And_focusQuestion()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("content");
        json.Should().Contain("focusQuestion");
    }
}
