using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class MapReduceToolTests
{
    private static MapReduceTool CreateTool(string modelId = "test-model")
    {
        return new MapReduceTool(
            new Mock<IModelProvider>().Object, modelId,
            new ToolRegistry(), new Mock<ISandboxFactory>().Object);
    }

    private static ToolContext CreateContext() => new()
    {
        AgentId = "test-parent", CallId = "test-call",
        Sandbox = new Mock<ISandbox>().Object,
        SandboxOptions = new SandboxOptions { WorkingDirectory = "/tmp", EnforceBoundary = false },
    };

    [Fact] public void Name_Is_map_reduce() => CreateTool().Name.Should().Be("map_reduce");

    [Fact] public void Attributes_Are_ReadOnly() => CreateTool().Attributes.ReadOnly.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var result = await CreateTool(modelId).ExecuteAsync(
            new MapReduceArgs { Items = ["a"], MapTask = "do {item}", ReduceTask = "reduce" },
            CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Items_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new MapReduceArgs { Items = [], MapTask = "do {item}", ReduceTask = "reduce" },
            CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Items");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_MapTask_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new MapReduceArgs { Items = ["a"], MapTask = "", ReduceTask = "reduce" },
            CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("MapTask");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_ReduceTask_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new MapReduceArgs { Items = ["a"], MapTask = "do {item}", ReduceTask = "" },
            CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("ReduceTask");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Correct_ChunkCount_For_ChunkSize_Two()
    {
        // 3 items, ChunkSize=2 → 2 chunks; map tools stripped → map fails fast
        // reduce still runs (uses default tools)
        var result = await CreateTool().ExecuteAsync(
            new MapReduceArgs
            {
                Items = ["a", "b", "c"],
                MapTask = "summarise {item}",
                ReduceTask = "reduce all",
                ChunkSize = 2,
                MapTools = ["fs_write"],   // stripped → map fails fast
            }, CreateContext());

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"totalChunks\":2");
        json.Should().Contain("\"totalItems\":3");
    }

    [Fact]
    public void InputSchema_Contains_mapTask_And_reduceTask()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("mapTask");
        json.Should().Contain("reduceTask");
    }
}
