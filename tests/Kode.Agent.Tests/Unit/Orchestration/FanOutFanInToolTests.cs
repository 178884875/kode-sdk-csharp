using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class FanOutFanInToolTests
{
    private static FanOutFanInTool CreateTool(string modelId = "test-model")
    {
        return new FanOutFanInTool(
            new Mock<IModelProvider>().Object, modelId,
            new ToolRegistry(), new Mock<ISandboxFactory>().Object);
    }

    private static ToolContext CreateContext() => new()
    {
        AgentId = "test-parent", CallId = "test-call",
        Sandbox = new Mock<ISandbox>().Object,
        SandboxOptions = new SandboxOptions { WorkingDirectory = "/tmp", EnforceBoundary = false },
    };

    [Fact] public void Name_Is_fan_out_fan_in() => CreateTool().Name.Should().Be("fan_out_fan_in");

    [Fact] public void Attributes_Are_ReadOnly() => CreateTool().Attributes.ReadOnly.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var result = await CreateTool(modelId).ExecuteAsync(
            new FanOutFanInArgs
            {
                Tasks = [new ResearchTask { Task = "t" }],
                SynthesisTask = "synthesise",
            }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Tasks_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new FanOutFanInArgs { Tasks = [], SynthesisTask = "synthesise" }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("at least one task");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_SynthesisTask_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new FanOutFanInArgs
            {
                Tasks = [new ResearchTask { Task = "t" }],
                SynthesisTask = "   ",
            }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("SynthesisTask");
    }

    [Fact]
    public async Task ExecuteAsync_FanOut_Failure_Still_Passes_Context_To_Synthesis()
    {
        // fan-out tasks fail (tools stripped) but synthesis still runs with error notes
        var result = await CreateTool().ExecuteAsync(
            new FanOutFanInArgs
            {
                Tasks = [new ResearchTask { Name = "T1", Task = "t", Tools = ["fs_write"] }],
                SynthesisTask = "summarise",
                SynthesisTools = ["fs_write"],   // synthesis also stripped → synthesis fails
            }, CreateContext());

        result.Success.Should().BeTrue();   // Ok — caller reads detail
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("fanOut");
    }

    [Fact]
    public void InputSchema_Contains_synthesisTask()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("synthesisTask");
    }
}
