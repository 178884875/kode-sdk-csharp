using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class AskSpecialistToolTests
{
    private static AskSpecialistTool CreateTool(string modelId = "test-model")
    {
        return new AskSpecialistTool(
            new Mock<IModelProvider>().Object, modelId,
            new ToolRegistry(), new Mock<ISandboxFactory>().Object);
    }

    private static ToolContext CreateContext() => new()
    {
        AgentId = "test-parent", CallId = "test-call",
        Sandbox = new Mock<ISandbox>().Object,
        SandboxOptions = new SandboxOptions { WorkingDirectory = "/tmp", EnforceBoundary = false },
    };

    [Fact] public void Name_Is_ask_specialist() => CreateTool().Name.Should().Be("ask_specialist");

    [Fact] public void Attributes_Are_ReadOnly() => CreateTool().Attributes.ReadOnly.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var result = await CreateTool(modelId).ExecuteAsync(
            new AskSpecialistArgs { Task = "test", SpecialistRole = "expert" }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_SpecialistRole_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new AskSpecialistArgs { Task = "test", SpecialistRole = "   " }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("SpecialistRole");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_All_Tools_Stripped()
    {
        var result = await CreateTool().ExecuteAsync(
            new AskSpecialistArgs { Task = "test", SpecialistRole = "expert", Tools = ["channel_send", "inbox_create"] },
            CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No allowed tools remain");
    }

    [Fact]
    public void InputSchema_Contains_specialistRole()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("specialistRole");
    }
}
